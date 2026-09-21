using System.Net;
using System.Text;
using System.Text.Json;
using Overlay.Translation;

static void Check(bool condition, string description)
{ if (!condition) throw new Exception(description); Console.WriteLine("PASS " + description); }
static async Task Fails(Func<Task> action, string expected)
{
    try { await action(); throw new Exception("Expected failure"); }
    catch (TranslationException e) { Check(e.Message.Contains(expected), expected); }
}
var gate = new StableTextGate();
Check(!gate.Observe("Hello", TimeSpan.Zero).Ready, "First automatic sample waits");
Check(!gate.Observe("Hello!", TimeSpan.FromSeconds(1)).Ready, "Typewriter changes restart stabilization");
Check(gate.Observe("Hello!", TimeSpan.FromSeconds(2)).Ready, "Repeated stable text emits");
Check(!gate.Observe("Hello!", TimeSpan.FromSeconds(3)).Ready, "Unchanged text emits only once");
Check(gate.Observe("", TimeSpan.FromSeconds(4)).Changed, "Blank invalidates old text");
Check(!gate.Observe("", TimeSpan.FromSeconds(5)).Ready, "Blank never translates");
Check(gate.Observe("Hello!", TimeSpan.FromSeconds(6), true).Ready, "Manual read bypasses wait");
gate.Reset();
Check(!gate.Observe("Hello!", TimeSpan.FromSeconds(7)).Ready, "Resume requires fresh stability");

const string good = """
{"status":"completed","output":[{"type":"reasoning"},{"type":"message","content":[{"type":"output_text","text":"你好，博士。"}]}],"usage":{"input_tokens":20,"output_tokens":8,"input_tokens_details":{"cached_tokens":10}}}
""";
var handler = new FakeHandler(good);
using var http = new HttpClient(handler);
var api = new LunaTranslator(http);
var target = TargetLanguage.SimplifiedChinese;
var result = await api.TranslateAsync("Hello, doctor.", target, "test-key", default);
Check(result.Text == "你好，博士。" && result.InputTokens == 20 && result.CachedInputTokens == 10, "Responses output and usage parsed");
using (var body = JsonDocument.Parse(handler.Body!))
{
    Check(body.RootElement.GetProperty("model").GetString() == "gpt-5.6-luna", "Luna model selected");
    Check(!body.RootElement.GetProperty("store").GetBoolean() && body.RootElement.GetProperty("reasoning").GetProperty("effort").GetString() == "none", "Storage disabled and no reasoning");
    Check(!body.RootElement.TryGetProperty("tools", out _), "No agent tools");
}
Check(handler.Uri == "https://api.openai.com/v1/responses" && handler.Auth == "Bearer test-key", "Fixed endpoint and authorization");
int calls = handler.Calls;
await api.TranslateAsync("  ", target, "test-key", default);
Check(handler.Calls == calls, "Blank text costs no request");
await Fails(() => api.TranslateAsync(new string('a', 6001), target, "test-key", default), "smaller region");
await Fails(() => api.TranslateAsync("Hello", new("unsupported", "Unknown"), "test-key", default), "Unsupported");
handler.Status = HttpStatusCode.Unauthorized;
handler.Payload = "private provider diagnostics";
await Fails(() => api.TranslateAsync("Hello", target, "test-key", default), "API key rejected");
handler.Status = HttpStatusCode.TooManyRequests;
await Fails(() => api.TranslateAsync("Hello", target, "test-key", default), "quota or rate");
handler.Status = HttpStatusCode.OK;
handler.Payload = "{\"status\":\"incomplete\"}";
await Fails(() => api.TranslateAsync("Hello", target, "test-key", default), "incomplete");
handler.Payload = "not json";
await Fails(() => api.TranslateAsync("Hello", target, "test-key", default), "Unexpected");
handler.Payload = "{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\"}]}]}";
await Fails(() => api.TranslateAsync("Hello", target, "test-key", default), "could not translate");
handler.Payload = "{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"你好\"}]}]}";
Check((await api.TranslateAsync("Hi", target, "test-key", default)).InputTokens is null, "Missing usage stays unknown");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try { await api.TranslateAsync("Hi", target, "test-key", cancelled.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { Check(true, "Cancellation preserved"); }
}

handler.WaitForCancellation = true;
var shortTimeoutApi = new LunaTranslator(http, TimeSpan.FromMilliseconds(20));
await Fails(() => shortTimeoutApi.TranslateAsync("Hello", target, "test-key", default), "timed out");
handler.WaitForCancellation = false;

// Drive a single synchronization context, as the WPF dispatcher does.
var pump = new Pump();
pump.Run(async () =>
{
    var fake = new ControlledTranslator();
    var queue = new TranslationQueue(fake);
    var shown = new List<string>();
    queue.Updated += (_, _, r) => { if (r is not null) shown.Add(r.Text); };
    queue.Submit("A", target, "fake");
    await Task.Yield();
    queue.Submit("B", target, "fake");
    queue.Submit("C", target, "fake");
    fake.Replies[0].SetResult(new("old A", TimeSpan.Zero, 1, 1)); // deliberately ignores cancellation
    await Task.Yield();
    Check(fake.Inputs.SequenceEqual(new[] { "A", "C" }), "Only newest pending request runs");
    fake.Replies[1].SetResult(new("new C", TimeSpan.Zero, 1, 1));
    await queue.Completion;
    Check(shown.SequenceEqual(new[] { "new C" }), "Old response cannot overwrite current generation");
    queue.Submit("C", target, "fake");
    await queue.Completion;
    Check(queue.Attempts == 2 && queue.CacheHits == 1, "Cache prevents repeat network calls");
    queue.Invalidate(true);
    queue.Submit("C", target, "fake");
    await Task.Yield();
    queue.Invalidate(); // stop/target/key change
    fake.Replies[2].SetResult(new("stale", TimeSpan.Zero));
    await queue.Completion;
    Check(!shown.Contains("stale") && queue.UnknownUsage == 1, "Stop suppresses output; missing usage recorded");
    queue.SessionLimit = 3;
    string error = "";
    queue.Updated += (_, s, r) => { if (r is null) error = s; };
    queue.Submit("D", target, "fake");
    await queue.Completion;
    Check(queue.Attempts == 3 && error.Contains("limit reached"), "Session cap blocks dispatch");
    var fastQueue = new TranslationQueue(new ImmediateTranslator());
    string rateError = "";
    fastQueue.Updated += (_, message, _) => rateError = message;
    for (int i = 0; i < 31; i++)
    {
        fastQueue.Submit("line " + i, target, "fake");
        await fastQueue.Completion;
    }
    Check(fastQueue.Attempts == 30 && rateError.Contains("30 requests/minute"), "Minute cap blocks a burst");
    var failedQueue = new TranslationQueue(new FailingTranslator());
    failedQueue.Submit("line", target, "fake");
    await failedQueue.Completion;
    Check(failedQueue.Attempts == 1 && failedQueue.UnknownUsage == 1, "Failed attempt consumes quota; usage remains unknown");
});
Console.WriteLine("Translation tests passed. No live API calls.");

sealed class FakeHandler(string payload) : HttpMessageHandler
{
    public string Payload = payload;
    public HttpStatusCode Status = HttpStatusCode.OK;
    public int Calls;
    public bool WaitForCancellation;
    public string? Body, Uri, Auth;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (WaitForCancellation) await Task.Delay(Timeout.Infinite, cancellationToken);
        Calls++; Uri = request.RequestUri!.ToString(); Auth = request.Headers.Authorization?.ToString();
        Body = await request.Content!.ReadAsStringAsync(cancellationToken);
        return new(Status) { Content = new StringContent(Payload, Encoding.UTF8, "application/json") };
    }
}
sealed class ControlledTranslator : ITranslator
{
    public List<string> Inputs = [];
    public List<TaskCompletionSource<TranslationResult>> Replies = [];
    public Task<TranslationResult> TranslateAsync(string text, TargetLanguage target, string apiKey, CancellationToken token)
    {
        Inputs.Add(text);
        var reply = new TaskCompletionSource<TranslationResult>(); Replies.Add(reply); return reply.Task;
    }
}
sealed class Pump : SynchronizationContext
{
    private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object?)> _work = new();
    public override void Post(SendOrPostCallback d, object? state) => _work.Add((d, state));
    public void Run(Func<Task> action)
    {
        var previous = Current; SetSynchronizationContext(this);
        try
        {
            var task = action();
            while (!task.IsCompleted)
            {
                if (_work.TryTake(out var item, 100)) item.Item1(item.Item2);
            }
            task.GetAwaiter().GetResult();
        }
        finally { SetSynchronizationContext(previous); }
    }
}

sealed class ImmediateTranslator : ITranslator
{
    public Task<TranslationResult> TranslateAsync(string text, TargetLanguage target, string apiKey, CancellationToken token)
        => Task.FromResult(new TranslationResult("译文", TimeSpan.Zero, 1, 1));
}
sealed class FailingTranslator : ITranslator
{
    public Task<TranslationResult> TranslateAsync(string text, TargetLanguage target, string apiKey, CancellationToken token)
        => Task.FromException<TranslationResult>(new TranslationException("Offline"));
}
