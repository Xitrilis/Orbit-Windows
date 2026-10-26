using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Orbit;

static class GeminiClientTests
{
    static int passed;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Pass(string name) { passed++; Console.WriteLine("PASS " + name); }
    sealed class Fake : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Run;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage m, CancellationToken c) => Run(m, c);
    }
    static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) => new HttpResponseMessage(code) { Content = new StringContent(json) };
    static async Task Main()
    {
        var history = new List<AiTurn> { new AiTurn { role = "user", text = "Вопрос" }, new AiTurn { role = "model", text = "Ответ" } };
        var text = GeminiClient.BuildRequest("Portal 2", "steam", "Как пройти?", history, null, null, true);
        Check(!text.ToString().Contains("inlineData"), "Text-only request leaked an image");
        Check(((JArray)text["contents"]).Count == 3, "Conversation missing");
        Check(text["systemInstruction"].ToString().Contains("Без сюжетных спойлеров"), "Spoiler instruction missing");
        Pass("text-only request, bounded conversation and spoiler preference");
        var image = GeminiClient.BuildRequest("Portal 2", "steam", "Что на кадре?", history, new byte[] { 1, 2, 3 }, "LOCAL REPORT", false);
        var parts = (JArray)image["contents"].Last["parts"];
        Check((string)parts[0]["inlineData"]["mimeType"] == "image/jpeg" && (string)parts[0]["inlineData"]["data"] == "AQID", "Image payload malformed");
        Check(parts.ToString().Contains("LOCAL REPORT"), "Requested report missing");
        Check(!text.ToString().Contains("LOCAL REPORT"), "Unrequested report leaked");
        Pass("explicit frame and report attachments");
        var handler = new Fake { Run = async (m, c) => {
            Check(m.RequestUri.Scheme == "https" && m.RequestUri.Host == "generativelanguage.googleapis.com", "Unexpected destination");
            Check(!m.RequestUri.ToString().Contains("not-a-real-key"), "Key in URL");
            Check(m.Headers.GetValues("x-goog-api-key").Single() == "not-a-real-key", "Auth header missing");
            Check(JObject.Parse(await m.Content.ReadAsStringAsync())["contents"] != null, "Request JSON missing");
            return Json("{\"candidates\":[{\"content\":{\"parts\":[{\"thought\":true,\"text\":\"PRIVATE TEST THOUGHT\"},{\"text\":\"Открой дверь.\"}]}}]}");
        }};
        using (var client = new GeminiClient(handler)) Check(await client.Ask("not-a-real-key", "gemini-test-flash", text, CancellationToken.None) == "Открой дверь.", "Visible answer incorrect");
        Pass("HTTPS destination, header authentication, response extraction without thought parts");
        int page = 0;
        handler = new Fake { Run = (m, c) => Task.FromResult(++page == 1 ? Json("{\"models\":[{\"name\":\"models/gemini-test-flash\",\"displayName\":\"Test flash\",\"supportedGenerationMethods\":[\"generateContent\"]},{\"name\":\"models/gemini-test-tts\",\"supportedGenerationMethods\":[\"generateContent\"]}],\"nextPageToken\":\"next page\"}") : Json("{\"models\":[{\"name\":\"models/gemini-test-pro\",\"supportedGenerationMethods\":[\"generateContent\"]}]}")) };
        using (var client = new GeminiClient(handler)) { var models = await client.Models("not-a-real-key", CancellationToken.None); Check(models.Count == 2 && page == 2 && models.All(m => !m.id.Contains("tts")), "Model discovery incorrect"); }
        Pass("model discovery, filtering and pagination");
        handler = new Fake { Run = (m, c) => Task.FromResult(Json("{\"error\":{\"message\":\"not-a-real-key PRIVATE SERVER DATA\"}}", (HttpStatusCode)429)) };
        using (var client = new GeminiClient(handler)) { try { await client.Ask("not-a-real-key", "gemini-test-flash", text, CancellationToken.None); throw new Exception("Expected quota error"); } catch (InvalidOperationException e) { Check(e.Message.Contains("квота") && !e.Message.Contains("PRIVATE") && !e.Message.Contains("not-a-real-key"), "Unsafe or unhelpful API error"); } }
        Pass("quota errors do not expose server response or API key");
        handler = new Fake { Run = (m, c) => Task.FromResult(Json("{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}")) };
        using (var client = new GeminiClient(handler)) { try { await client.Ask("not-a-real-key", "gemini-test-flash", text, CancellationToken.None); throw new Exception("Expected empty answer error"); } catch (InvalidOperationException) { } }
        Pass("blocked or empty responses produce an explicit error");
        handler = new Fake { Run = async (m, c) => { await Task.Delay(10000, c); return Json("{}"); } };
        using (var client = new GeminiClient(handler)) using (var cancel = new CancellationTokenSource(30)) { try { await client.Ask("not-a-real-key", "gemini-test-flash", text, cancel.Token); throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { } }
        Pass("in-flight request cancellation");
        foreach (var invalid in new[] { "", "../../other-host", "https://example.com", "gemini-x?key=secret" }) { try { GeminiClient.ModelId(invalid); throw new Exception("Accepted invalid model"); } catch (ArgumentException) { } }
        try { GeminiClient.BuildRequest("Game", "steam", "", history, null, null, true); throw new Exception("Accepted empty question"); } catch (ArgumentException) { }
        Pass("model and question validation");
        Console.WriteLine(passed + " Gemini client checks passed. HTTP transport was mocked; no real key or API request was used.");
    }
}
