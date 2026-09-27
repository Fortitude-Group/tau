using System.Text.Json.Nodes;
using Tau.Contract;

namespace Tau.Conformance;

/// <summary>A model disagreement: both sides gave a structurally valid but different answer.</summary>
internal sealed record Disagreement(string Question, string Kind, string TauValue, string PeerValue);

/// <summary>
/// Compares Tau's and a peer's answers for a fixture, once both sides are already known to be
/// contractually valid, and records only the headline value that differs (the choice, the score,
/// or the noul) — never a failure, per R-13: different models are expected to disagree.
/// </summary>
internal static class Disagree
{
    public static IReadOnlyList<Disagreement> Compare(DecisionRequest request, string tauBody, string peerBody)
    {
        var result = new List<Disagreement>();

        if (JsonNode.Parse(tauBody) is not JsonObject tauRoot || tauRoot["answers"] is not JsonObject tauAnswers
            || JsonNode.Parse(peerBody) is not JsonObject peerRoot || peerRoot["answers"] is not JsonObject peerAnswers)
        {
            return result;
        }

        foreach (var (key, question) in request.Questions)
        {
            if (tauAnswers[key] is not JsonObject tau || peerAnswers[key] is not JsonObject peer)
            {
                continue;
            }

            switch (question)
            {
                case ChoiceQuestion:
                    CompareString(key, "choice", "choice", tau, peer, result);
                    break;
                case ScoreQuestion:
                    CompareNumber(key, "score", "score", tau, peer, result);
                    break;
                case NoulQuestion:
                    CompareNumber(key, "noul", "noul", tau, peer, result);
                    break;
            }
        }

        return result;
    }

    private static void CompareString(
        string question, string kind, string field, JsonObject tau, JsonObject peer, List<Disagreement> into)
    {
        var t = JsonHelpers.AsString(tau[field]);
        var p = JsonHelpers.AsString(peer[field]);
        if (t is not null && p is not null && t != p)
        {
            into.Add(new Disagreement(question, kind, t, p));
        }
    }

    private static void CompareNumber(
        string question, string kind, string field, JsonObject tau, JsonObject peer, List<Disagreement> into)
    {
        var t = JsonHelpers.AsDouble(tau[field]);
        var p = JsonHelpers.AsDouble(peer[field]);
        if (t is not null && p is not null && Math.Abs(t.Value - p.Value) > 1e-6)
        {
            into.Add(new Disagreement(question, kind, t.Value.ToString("0.####"), p.Value.ToString("0.####")));
        }
    }
}
