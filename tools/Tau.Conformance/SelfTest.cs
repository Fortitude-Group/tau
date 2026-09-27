using Tau.Contract;

namespace Tau.Conformance;

/// <summary>
/// Runs the response checker (<see cref="ResponseValidator"/>) against a handful of canned good and
/// bad responses with a known-correct verdict, so the checker itself is exercised without needing
/// either server running. Exits non-zero if any verdict is wrong.
/// </summary>
internal static class SelfTest
{
    private sealed record Case(string Name, string RequestJson, string ResponseJson, bool Strict, bool ExpectOk);

    private const string NoulRequest =
        """{"model":"m","state":"s","questions":{"q":{"type":"noul","instructions":"i"}}}""";

    private const string ChoiceRequest =
        """{"model":"m","state":"s","questions":{"q":{"type":"choice","instructions":"i","criteria":{"a":"A","b":"B"}}}}""";

    private const string ScoreRequest =
        """{"model":"m","state":"s","questions":{"q":{"type":"score","instructions":"i","criteria":["lo","mid","hi"]}}}""";

    private const string TwoNoulRequest =
        """{"model":"m","state":"s","questions":{"q1":{"type":"noul","instructions":"i"},"q2":{"type":"noul","instructions":"i"}}}""";

    private static readonly Case[] Cases =
    [
        new(
            "good noul answer",
            NoulRequest,
            """{"model":"m","answers":{"q":{"type":"noul","noul":0.42}},"usage":{"input_tokens":10,"output_tokens":0}}""",
            true, true),

        new(
            "good choice answer",
            ChoiceRequest,
            """{"model":"m","answers":{"q":{"type":"choice","choice":"a","probabilities":{"a":0.7,"b":0.3},"confidence":0.4}},"usage":{"input_tokens":10,"output_tokens":0}}""",
            true, true),

        new(
            "good score answer",
            ScoreRequest,
            """{"model":"m","answers":{"q":{"type":"score","score":1.2,"legend":{"0":"lo","1":"mid","2":"hi"},"probabilities":{"0":0.2,"1":0.3,"2":0.5},"confidence":0.5}},"usage":{"input_tokens":10,"output_tokens":0}}""",
            true, true),

        new(
            "bad: choice probabilities do not sum to 1",
            ChoiceRequest,
            """{"model":"m","answers":{"q":{"type":"choice","choice":"a","probabilities":{"a":0.7,"b":0.1},"confidence":0.4}},"usage":{"input_tokens":10,"output_tokens":0}}""",
            true, false),

        new(
            "bad: choice value not in criteria",
            ChoiceRequest,
            """{"model":"m","answers":{"q":{"type":"choice","choice":"c","probabilities":{"a":0.6,"b":0.4},"confidence":0.4}},"usage":{"input_tokens":10,"output_tokens":0}}""",
            true, false),

        new(
            "bad: score outside its own range",
            ScoreRequest,
            """{"model":"m","answers":{"q":{"type":"score","score":5.0,"legend":{"0":"lo","1":"mid","2":"hi"},"probabilities":{"0":0.2,"1":0.3,"2":0.5},"confidence":0.5}},"usage":{"input_tokens":10,"output_tokens":0}}""",
            true, false),

        new(
            "bad: missing answer for one of two questions",
            TwoNoulRequest,
            """{"model":"m","answers":{"q1":{"type":"noul","noul":0.5}},"usage":{"input_tokens":10,"output_tokens":0}}""",
            true, false),

        new(
            "bad: unexpected answer type for the question asked",
            NoulRequest,
            """{"model":"m","answers":{"q":{"type":"choice","choice":"a"}},"usage":{"input_tokens":10,"output_tokens":0}}""",
            true, false),

        new(
            "top-level extension field, strict",
            NoulRequest,
            """{"model":"m","answers":{"q":{"type":"noul","noul":0.42}},"usage":{"input_tokens":10,"output_tokens":0},"x-debug":"trace-1"}""",
            true, false),

        new(
            "top-level extension field, lenient (peer extension, not a failure)",
            NoulRequest,
            """{"model":"m","answers":{"q":{"type":"noul","noul":0.42}},"usage":{"input_tokens":10,"output_tokens":0},"x-debug":"trace-1"}""",
            false, true),

        new(
            "extra field inside an answer, strict",
            ChoiceRequest,
            """{"model":"m","answers":{"q":{"type":"choice","choice":"a","probabilities":{"a":0.7,"b":0.3},"confidence":0.4,"reasoning":"picked a"}},"usage":{"input_tokens":10,"output_tokens":0}}""",
            true, false),

        new(
            "extra field inside an answer, lenient (peer extension, not a failure)",
            ChoiceRequest,
            """{"model":"m","answers":{"q":{"type":"choice","choice":"a","probabilities":{"a":0.7,"b":0.3},"confidence":0.4,"reasoning":"picked a"}},"usage":{"input_tokens":10,"output_tokens":0}}""",
            false, true),
    ];

    public static int Run()
    {
        var failures = 0;

        foreach (var testCase in Cases)
        {
            if (!ContractParser.TryParse(testCase.RequestJson, out var request, out var problems))
            {
                Console.Error.WriteLine($"[SELF-TEST SETUP BUG] '{testCase.Name}': its own request fixture is invalid: "
                    + string.Join("; ", problems.Select(p => $"{p.Path}: {p.Problem}")));
                failures++;
                continue;
            }

            var outcome = ResponseValidator.ValidateSuccessBody(testCase.ResponseJson, request!, testCase.Strict);
            var pass = outcome.ContractOk == testCase.ExpectOk;

            Console.WriteLine(
                $"[{(pass ? "OK" : "WRONG VERDICT")}] {testCase.Name} "
                + $"(strict={testCase.Strict}, expected {(testCase.ExpectOk ? "pass" : "fail")}, "
                + $"got {(outcome.ContractOk ? "pass" : "fail")})");

            if (!pass)
            {
                failures++;
                foreach (var error in outcome.Errors)
                {
                    Console.WriteLine($"    - {error}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? $"self-test: all {Cases.Length} canned responses got the expected verdict"
            : $"self-test: {failures} of {Cases.Length} canned responses got the WRONG verdict");

        return failures == 0 ? 0 : 1;
    }
}
