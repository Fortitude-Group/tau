using System.Net;
using System.Text;

namespace Tau.Client.Tests;

/// <summary>Canned <c>/v1/systemone</c> response bodies used across the test suite.</summary>
internal static class Responses
{
    public static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    public static HttpResponseMessage Text(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/plain"),
    };

    public static HttpResponseMessage Choice(string questionKey, string choice, string probabilitiesJson, double confidence, string model = "jev-1.13.0")
    {
        var body = "{\"model\":\"" + model + "\",\"answers\":{\"" + questionKey + "\":{\"type\":\"choice\",\"choice\":\""
            + choice + "\",\"probabilities\":" + probabilitiesJson + ",\"confidence\":" + confidence
            + "}},\"usage\":{\"input_tokens\":12,\"output_tokens\":0}}";
        return Json(HttpStatusCode.OK, body);
    }

    public static HttpResponseMessage Score(string questionKey, double score, string legendJson, string probabilitiesJson, double confidence, string model = "jev-1.13.0")
    {
        var body = "{\"model\":\"" + model + "\",\"answers\":{\"" + questionKey + "\":{\"type\":\"score\",\"score\":"
            + score + ",\"legend\":" + legendJson + ",\"probabilities\":" + probabilitiesJson + ",\"confidence\":"
            + confidence + "}},\"usage\":{\"input_tokens\":12,\"output_tokens\":0}}";
        return Json(HttpStatusCode.OK, body);
    }

    public static HttpResponseMessage Noul(string questionKey, double noul, string model = "jev-1.13.0")
    {
        var body = "{\"model\":\"" + model + "\",\"answers\":{\"" + questionKey + "\":{\"type\":\"noul\",\"noul\":"
            + noul + "}},\"usage\":{\"input_tokens\":12,\"output_tokens\":0}}";
        return Json(HttpStatusCode.OK, body);
    }

    public static HttpResponseMessage ValidationError(params (string Path, string Problem)[] problems)
    {
        var details = string.Join(",", problems.Select(p => "{\"path\":\"" + p.Path + "\",\"problem\":\"" + p.Problem + "\"}"));
        var body = "{\"error\":{\"type\":\"validation_error\",\"message\":\"the request was invalid\",\"details\":[" + details + "]}}";
        return Json(HttpStatusCode.UnprocessableEntity, body);
    }
}
