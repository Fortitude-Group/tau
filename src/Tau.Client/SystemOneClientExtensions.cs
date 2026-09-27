using System.Globalization;
using System.Text.Json.Nodes;
using Tau.Contract;

namespace Tau.Client;

/// <summary>
/// Typed, single-question helpers built on <see cref="ISystemOneClient.SystemOneAsync(DecisionRequest, CancellationToken)"/>. Each
/// helper builds a <see cref="DecisionRequest"/> with exactly one question (keyed
/// <c>"decision"</c>), sends it, and interprets the matching answer — so any implementation of
/// <see cref="ISystemOneClient"/> (the real client, or a test double) gets them for free.
/// </summary>
public static class SystemOneClientExtensions
{
    private const string QuestionKey = "decision";
    private const string DefaultModel = "jev-latest";

    /// <summary>
    /// Asks a choice question whose options are the members of <typeparamref name="TEnum"/>, and
    /// returns the chosen member plus the full probability distribution.
    /// </summary>
    /// <typeparam name="TEnum">
    /// The enum type whose members are the question's options. Each member's wire name is its
    /// <see cref="System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute"/> or
    /// <see cref="System.Runtime.Serialization.EnumMemberAttribute"/> name if present, otherwise
    /// its plain member name (see <see cref="EnumWireNames"/>).
    /// </typeparam>
    /// <param name="client">The client to send the request through.</param>
    /// <param name="state">The state to evaluate: text, structured data, or <c>null</c>.</param>
    /// <param name="instructions">The instructions given to the model for this question.</param>
    /// <param name="descriptions">
    /// An optional description for each enum member the model should be told about. Members
    /// absent from this map are still offered as options, with no description.
    /// </param>
    /// <param name="model">The model id or auto-routing alias to request. Defaults to <c>"jev-latest"</c>.</param>
    /// <param name="ct">A token that cancels the request.</param>
    /// <returns>The chosen enum member, its probability distribution, and its confidence.</returns>
    /// <exception cref="SystemOneProtocolException">
    /// The response had no choice answer for this question, or its chosen key matched none of
    /// <typeparamref name="TEnum"/>'s wire names.
    /// </exception>
    public static async Task<Decision<TEnum>> DecideAsync<TEnum>(
        this ISystemOneClient client,
        JsonNode? state,
        string instructions,
        IReadOnlyDictionary<TEnum, string>? descriptions = null,
        string model = DefaultModel,
        CancellationToken ct = default)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(instructions);

        var members = EnumWireNames.Get<TEnum>();
        var criteria = new List<KeyValuePair<string, JsonNode?>>(members.Count);
        foreach (var (value, name) in members)
        {
            JsonNode? description = descriptions is not null && descriptions.TryGetValue(value, out var text)
                ? JsonValue.Create(text)
                : null;
            criteria.Add(new KeyValuePair<string, JsonNode?>(name, description));
        }

        var question = new ChoiceQuestion
        {
            Instructions = JsonValue.Create(instructions)!,
            Criteria = criteria,
        };

        var request = BuildRequest(model, state, question);
        var response = await client.SystemOneAsync(request, ct).ConfigureAwait(false);

        if (!response.Answers.TryGetValue(QuestionKey, out var answer) || answer is not ChoiceAnswer choiceAnswer)
        {
            throw new SystemOneProtocolException(
                $"expected a choice answer for question '{QuestionKey}', but the response had none.");
        }

        var byName = new Dictionary<string, TEnum>(members.Count);
        var probabilities = new Dictionary<TEnum, double>(members.Count);
        foreach (var (value, name) in members)
        {
            byName[name] = value;
            probabilities[value] = choiceAnswer.Probabilities.TryGetValue(name, out var p) ? p : 0.0;
        }

        if (!byName.TryGetValue(choiceAnswer.Choice, out var chosen))
        {
            throw new SystemOneProtocolException(
                $"the server chose '{choiceAnswer.Choice}', which matches none of {typeof(TEnum).Name}'s members.");
        }

        return new Decision<TEnum>(chosen, probabilities, choiceAnswer.Confidence, choiceAnswer);
    }

    /// <summary>Convenience overload of <see cref="DecideAsync{TEnum}(ISystemOneClient,JsonNode?,string,IReadOnlyDictionary{TEnum,string}?,string,CancellationToken)"/> taking the state as plain text.</summary>
    public static Task<Decision<TEnum>> DecideAsync<TEnum>(
        this ISystemOneClient client,
        string state,
        string instructions,
        IReadOnlyDictionary<TEnum, string>? descriptions = null,
        string model = DefaultModel,
        CancellationToken ct = default)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(state);
        return client.DecideAsync((JsonNode?)JsonValue.Create(state), instructions, descriptions, model, ct);
    }

    /// <summary>
    /// Asks a score question over an ordered set of levels, and returns the probability-weighted
    /// score plus a rounded level index.
    /// </summary>
    /// <param name="client">The client to send the request through.</param>
    /// <param name="state">The state to evaluate: text, structured data, or <c>null</c>.</param>
    /// <param name="instructions">The instructions given to the model for this question.</param>
    /// <param name="levels">The scale's levels, in order, index 0 first (2 to 10 entries).</param>
    /// <param name="model">The model id or auto-routing alias to request. Defaults to <c>"jev-latest"</c>.</param>
    /// <param name="ct">A token that cancels the request.</param>
    /// <returns>The score, its rounded level, the per-level probabilities, confidence and legend.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="levels"/> has fewer than 2 or more than 10 entries.</exception>
    /// <exception cref="SystemOneProtocolException">The response had no score answer for this question.</exception>
    public static async Task<ScoreResult> ScoreAsync(
        this ISystemOneClient client,
        JsonNode? state,
        string instructions,
        IReadOnlyList<string> levels,
        string model = DefaultModel,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(levels);
        if (levels.Count is < 2 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(levels), levels.Count, "a score question needs 2 to 10 levels.");
        }

        var question = new ScoreQuestion
        {
            Instructions = JsonValue.Create(instructions)!,
            Criteria = levels.Select(level => (JsonNode)JsonValue.Create(level)!).ToList(),
        };

        var request = BuildRequest(model, state, question);
        var response = await client.SystemOneAsync(request, ct).ConfigureAwait(false);

        if (!response.Answers.TryGetValue(QuestionKey, out var answer) || answer is not ScoreAnswer scoreAnswer)
        {
            throw new SystemOneProtocolException(
                $"expected a score answer for question '{QuestionKey}', but the response had none.");
        }

        var probabilities = new List<double>(levels.Count);
        var legend = new List<string>(levels.Count);
        for (var i = 0; i < levels.Count; i++)
        {
            var key = i.ToString(CultureInfo.InvariantCulture);
            probabilities.Add(scoreAnswer.Probabilities.TryGetValue(key, out var p) ? p : 0.0);
            legend.Add(scoreAnswer.Legend.TryGetValue(key, out var text) ? text : levels[i]);
        }

        var level = (int)Math.Clamp(Math.Round(scoreAnswer.Score, MidpointRounding.AwayFromZero), 0, levels.Count - 1);

        return new ScoreResult(scoreAnswer.Score, level, probabilities, scoreAnswer.Confidence, legend)
        {
            Raw = scoreAnswer,
        };
    }

    /// <summary>Convenience overload of <see cref="ScoreAsync(ISystemOneClient,JsonNode?,string,IReadOnlyList{string},string,CancellationToken)"/> taking the state as plain text.</summary>
    public static Task<ScoreResult> ScoreAsync(
        this ISystemOneClient client,
        string state,
        string instructions,
        IReadOnlyList<string> levels,
        string model = DefaultModel,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        return client.ScoreAsync((JsonNode?)JsonValue.Create(state), instructions, levels, model, ct);
    }

    /// <summary>
    /// Asks a yes/no question and returns the probability that the statement is true. Use
    /// <see cref="NoulDetailedAsync(ISystemOneClient,JsonNode?,string,string?,string?,string,CancellationToken)"/>
    /// for the underlying <see cref="NoulAnswer"/> as well.
    /// </summary>
    /// <param name="client">The client to send the request through.</param>
    /// <param name="state">The state to evaluate: text, structured data, or <c>null</c>.</param>
    /// <param name="instructions">The instructions given to the model for this question.</param>
    /// <param name="trueDescription">An optional description of what "true" means for this question.</param>
    /// <param name="falseDescription">An optional description of what "false" means for this question.</param>
    /// <param name="model">The model id or auto-routing alias to request. Defaults to <c>"jev-latest"</c>.</param>
    /// <param name="ct">A token that cancels the request.</param>
    /// <returns>The probability that the statement is true, from 0 (no) to 1 (yes).</returns>
    public static async Task<double> NoulAsync(
        this ISystemOneClient client,
        JsonNode? state,
        string instructions,
        string? trueDescription = null,
        string? falseDescription = null,
        string model = DefaultModel,
        CancellationToken ct = default)
    {
        var result = await client.NoulDetailedAsync(state, instructions, trueDescription, falseDescription, model, ct)
            .ConfigureAwait(false);
        return result.Value;
    }

    /// <summary>Convenience overload of <see cref="NoulAsync(ISystemOneClient,JsonNode?,string,string?,string?,string,CancellationToken)"/> taking the state as plain text.</summary>
    public static Task<double> NoulAsync(
        this ISystemOneClient client,
        string state,
        string instructions,
        string? trueDescription = null,
        string? falseDescription = null,
        string model = DefaultModel,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        return client.NoulAsync((JsonNode?)JsonValue.Create(state), instructions, trueDescription, falseDescription, model, ct);
    }

    /// <summary>
    /// Asks a yes/no question and returns the raw <see cref="NoulAnswer"/> alongside its
    /// probability. Most callers only need the probability: see <see cref="NoulAsync(ISystemOneClient,JsonNode?,string,string?,string?,string,CancellationToken)"/>.
    /// </summary>
    /// <param name="client">The client to send the request through.</param>
    /// <param name="state">The state to evaluate: text, structured data, or <c>null</c>.</param>
    /// <param name="instructions">The instructions given to the model for this question.</param>
    /// <param name="trueDescription">An optional description of what "true" means for this question.</param>
    /// <param name="falseDescription">An optional description of what "false" means for this question.</param>
    /// <param name="model">The model id or auto-routing alias to request. Defaults to <c>"jev-latest"</c>.</param>
    /// <param name="ct">A token that cancels the request.</param>
    /// <returns>The probability that the statement is true, and the raw answer that produced it.</returns>
    /// <exception cref="SystemOneProtocolException">The response had no noul answer for this question.</exception>
    public static async Task<NoulResult> NoulDetailedAsync(
        this ISystemOneClient client,
        JsonNode? state,
        string instructions,
        string? trueDescription = null,
        string? falseDescription = null,
        string model = DefaultModel,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(instructions);

        IReadOnlyDictionary<string, JsonNode?>? criteria = null;
        if (trueDescription is not null || falseDescription is not null)
        {
            var map = new Dictionary<string, JsonNode?>();
            if (trueDescription is not null)
            {
                map["true"] = JsonValue.Create(trueDescription);
            }

            if (falseDescription is not null)
            {
                map["false"] = JsonValue.Create(falseDescription);
            }

            criteria = map;
        }

        var question = new NoulQuestion
        {
            Instructions = JsonValue.Create(instructions)!,
            Criteria = criteria,
        };

        var request = BuildRequest(model, state, question);
        var response = await client.SystemOneAsync(request, ct).ConfigureAwait(false);

        if (!response.Answers.TryGetValue(QuestionKey, out var answer) || answer is not NoulAnswer noulAnswer)
        {
            throw new SystemOneProtocolException(
                $"expected a noul answer for question '{QuestionKey}', but the response had none.");
        }

        return new NoulResult(noulAnswer.Noul, noulAnswer);
    }

    private static DecisionRequest BuildRequest(string model, JsonNode? state, Question question) => new()
    {
        Model = model,
        State = state,
        Questions = new OrderedDictionary<string, Question> { [QuestionKey] = question },
    };
}
