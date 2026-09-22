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

gate.Reset();
Check(!gate.Observe("Wait", TimeSpan.Zero).Ready, "New moving text needs confirmation");
Check(gate.ConfirmationDue == TimeSpan.FromMilliseconds(350), "New text requests a 350 ms confirmation");
Check(!gate.Observe("Wait", TimeSpan.FromMilliseconds(349)).Ready, "Matching text cannot emit before confirmation delay");
Check(!gate.Observe("Wait here", TimeSpan.FromMilliseconds(350)).Ready, "Typewriter growth restarts the confirmation window");
Check(gate.ConfirmationDue == TimeSpan.FromMilliseconds(700), "Confirmation follows the latest text candidate");
Check(!gate.Observe("Wait here", TimeSpan.FromMilliseconds(699)).Ready, "Old candidate deadline cannot confirm new text");
Check(gate.Observe("Wait here", TimeSpan.FromMilliseconds(700)).Ready && gate.ConfirmationDue is null, "Matching text emits at the new deadline and stops requesting confirmation");
gate.Observe("Another line", TimeSpan.FromMilliseconds(900));
gate.Observe("", TimeSpan.FromMilliseconds(1000));
Check(gate.ConfirmationDue is null, "Blank cancels text confirmation");
gate.Observe("Another line", TimeSpan.FromMilliseconds(1100));
gate.Reset();
Check(gate.ConfirmationDue is null, "Reset cancels text confirmation");

var display = new TranslationDisplayState();
display.Show("Previous complete translation");
display.SourceChanged(hasText: true, TimeSpan.Zero);
Check(display.Text == "Previous complete translation" && display.IsPrevious, "New source keeps the complete previous translation, marked as previous");
Check(!display.ExpireBlank(TimeSpan.FromSeconds(30)) && display.Text.Length > 0, "Slow replacement does not erase the previous translation");
display.Show("Next complete translation");
Check(display.Text == "Next complete translation" && !display.IsPrevious, "Completed replacement becomes current without a blank interval");
display.SourceChanged(hasText: false, TimeSpan.FromSeconds(31));
Check(!display.ExpireBlank(TimeSpan.FromMilliseconds(31999)), "Brief blank frame preserves the translation");
display.SourceChanged(hasText: true, TimeSpan.FromMilliseconds(31999));
Check(!display.ExpireBlank(TimeSpan.FromSeconds(33)) && display.Text.Length > 0, "Returning text cancels blank expiry while replacement is pending");
display.SourceChanged(hasText: false, TimeSpan.FromSeconds(34));
display.SourceChanged(hasText: false, TimeSpan.FromMilliseconds(34500));
Check(display.ExpireBlank(TimeSpan.FromSeconds(35)) && display.Text.Length == 0 && !display.IsPrevious, "Repeated blank observations clear after one second without extending the grace period");
display.Show("Manual retry keeps this readable");
display.SourceChanged(hasText: false, TimeSpan.FromSeconds(36));
display.BeginUpdate();
Check(!display.ExpireBlank(TimeSpan.FromSeconds(38)) && display.IsPrevious && display.Text.Length > 0, "Manual retry retains text and cancels an earlier blank timer");
display.Reset();
Check(display.Text.Length == 0 && !display.IsPrevious, "Explicit stop or context change clears retained text immediately");

// Deterministic replay of local visual checks (no Windows capture or API needed).
var visual = new VisualReadGate();
byte[] still = new byte[10000];
visual.Observe(still, TimeSpan.Zero);
Check(!visual.TryRead(TimeSpan.FromMilliseconds(200), out _), "Initial image waits for settling");
visual.Observe(still, TimeSpan.FromMilliseconds(400));
Check(visual.TryRead(TimeSpan.FromMilliseconds(400), out bool settled) && settled, "Settled image starts OCR after 300 ms, rounded to capture tick");
for (int i = 1; i <= 300; i++)
{
    visual.Observe(still, TimeSpan.FromSeconds(i));
    if (visual.TryRead(TimeSpan.FromSeconds(i), out _)) throw new Exception("Unchanged scene repeated OCR");
}
Check(true, "Five minutes of identical frames produces no extra OCR");
byte[] noise = (byte[])still.Clone();
Array.Fill(noise, (byte)10);
visual.Observe(noise, TimeSpan.FromSeconds(301));
Check(!visual.TryRead(TimeSpan.FromSeconds(302), out _), "Low contrast noise ignored");
byte[] word = (byte[])still.Clone();
Array.Fill(word, (byte)200, 10, 20);
visual.Observe(word, TimeSpan.FromSeconds(303));
Check(visual.TryRead(TimeSpan.FromMilliseconds(303400), out settled) && settled, "Small word change schedules another settled read");

foreach (int changedPixels in new[] { 500, 999, 1000 })
{
    var fastVisual = new VisualReadGate();
    fastVisual.Observe(still, TimeSpan.Zero);
    Check(fastVisual.TryRead(TimeSpan.FromMilliseconds(400), out _), "Baseline settles before large change");
    byte[] changedFrame = (byte[])still.Clone();
    Array.Fill(changedFrame, (byte)200, 0, changedPixels);
    fastVisual.Observe(changedFrame, TimeSpan.FromMilliseconds(600));
    bool immediate = fastVisual.TryRead(TimeSpan.FromMilliseconds(600), out settled);
    Check(changedPixels == 1000 ? immediate && !settled : !immediate, $"Immediate threshold boundary: {changedPixels} of 10000 pixels");
    Check(!fastVisual.TryRead(TimeSpan.FromMilliseconds(600), out _), "Immediate trigger is consumed only once");
    fastVisual.Observe(changedFrame, TimeSpan.FromMilliseconds(1000));
    Check(fastVisual.TryRead(TimeSpan.FromMilliseconds(1000), out settled) && settled, "Early OCR still gets a settled confirmation");
    fastVisual.Observe(Enumerable.Repeat((byte)100, 10000).ToArray(), TimeSpan.FromMilliseconds(1200));
    // Simulate OCR being busy: observing more frames must retain the early-read request.
    fastVisual.Observe(still, TimeSpan.FromMilliseconds(1400));
    Check(fastVisual.TryRead(TimeSpan.FromMilliseconds(1400), out settled) && !settled, "Settling rearms immediate OCR and busy OCR retains the latest trigger");
    fastVisual.Observe(changedFrame, TimeSpan.FromMilliseconds(1600));
    Check(fastVisual.TryRead(TimeSpan.FromMilliseconds(2000), out settled) && settled, "Second motion burst settles");
    fastVisual.Observe(Enumerable.Repeat((byte)100, 10000).ToArray(), TimeSpan.FromMilliseconds(2200));
    fastVisual.Reset();
    fastVisual.Observe(still, TimeSpan.FromMilliseconds(2400));
    Check(!fastVisual.TryRead(TimeSpan.FromMilliseconds(2400), out _), "Reset clears pending immediate work");
}

visual.Reset();
for (int i = 0; i <= 6; i++)
{
    byte[] animated = Enumerable.Repeat((byte)(i * 40), 10000).ToArray();
    visual.Observe(animated, TimeSpan.FromMilliseconds(i * 200));
    bool read = visual.TryRead(TimeSpan.FromMilliseconds(i * 200), out settled);
    Check(i == 1 || i == 6 ? read && !settled : !read, $"Animation gets one immediate read then bounded fallback: tick {i}");
}
visual.Observe(Enumerable.Repeat((byte)240, 10000).ToArray(), TimeSpan.FromMilliseconds(1600));
Check(visual.TryRead(TimeSpan.FromMilliseconds(1600), out settled) && settled, "Final typewriter frame gets settled OCR after fallback");
gate.Reset();
Check(!gate.Observe("New dialogue", TimeSpan.Zero, visuallyStable: false).Ready, "Large image change starts OCR without claiming text is stable");
Check(gate.Observe("New dialogue", TimeSpan.Zero, visuallyStable: true).Ready, "Visual stability bypasses second OCR wait");
Check(!gate.Observe("New dialogue", TimeSpan.FromSeconds(1), visuallyStable: true).Ready, "Visual changes with identical OCR never re-emit translation");
Check(!gate.Observe("", TimeSpan.FromSeconds(2), visuallyStable: true).Ready, "Settled blank never translates");
visual.Reset();
visual.Observe(still, TimeSpan.FromSeconds(10));
Check(!visual.TryRead(TimeSpan.FromSeconds(10), out _), "Resume resets visual stability");

// Replay text confirmation while every captured frame continues changing.
var confirmationVisual = new VisualReadGate();
var confirmationText = new StableTextGate();
confirmationVisual.Observe(still, TimeSpan.Zero);
Check(confirmationVisual.TryRead(TimeSpan.FromMilliseconds(400), out _), "Confirmation replay starts from a settled scene");
confirmationVisual.Observe(Enumerable.Repeat((byte)40, 10000).ToArray(), TimeSpan.FromMilliseconds(600));
Check(confirmationVisual.TryRead(TimeSpan.FromMilliseconds(600), out settled) && !settled, "New dialogue starts with immediate unsettled OCR");
Check(!confirmationText.Observe("New dialogue", TimeSpan.FromMilliseconds(600)).Ready, "Early OCR requests confirmation before translation");
confirmationVisual.ConfirmTextAt(confirmationText.ConfirmationDue);
confirmationVisual.Observe(Enumerable.Repeat((byte)80, 10000).ToArray(), TimeSpan.FromMilliseconds(800));
Check(!confirmationVisual.TryRead(TimeSpan.FromMilliseconds(800), out _), "Confirmation does not run on every moving frame");
confirmationVisual.Observe(Enumerable.Repeat((byte)120, 10000).ToArray(), TimeSpan.FromMilliseconds(1000));
Check(confirmationVisual.TryRead(TimeSpan.FromMilliseconds(1000), out settled) && !settled, "Confirmation runs on the first tick after 350 ms despite continuous movement");
Check(confirmationText.Observe("New dialogue", TimeSpan.FromMilliseconds(1000), visuallyStable: settled).Ready, "Matching text translates after 400 ms instead of the next 900 ms fallback");
confirmationVisual.ConfirmTextAt(confirmationText.ConfirmationDue);
for (int tick = 6; tick <= 10; tick++)
{
    var now = TimeSpan.FromMilliseconds(tick * 200);
    confirmationVisual.Observe(Enumerable.Repeat((byte)(tick * 40), 10000).ToArray(), now);
    bool read = confirmationVisual.TryRead(now, out _);
    Check(tick == 10 ? read : !read, $"Confirmed text returns to bounded animation polling: tick {tick}");
}
confirmationVisual.ConfirmTextAt(TimeSpan.FromMilliseconds(2100));
confirmationVisual.Reset();
confirmationVisual.Observe(still, TimeSpan.FromMilliseconds(2200));
Check(!confirmationVisual.TryRead(TimeSpan.FromMilliseconds(2200), out _), "Visual reset cancels a scheduled confirmation");

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
