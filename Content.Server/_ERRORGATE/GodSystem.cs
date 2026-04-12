using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;
using Robust.Server.Audio;
using Robust.Shared.Asynchronous;


namespace Content.Server._ERRORGATE;

public sealed partial class GodSystem : GameRuleSystem<GodComponent>
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly ILogManager _log = default!;
    [Dependency] private readonly ITaskManager _taskManager = default!;

    private ISawmill _sawmill = default!;
    private HttpClient? _httpClient;
    private List<string> _actionLogs = new();

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _log.GetSawmill("god");

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:1234/"),
            Timeout = TimeSpan.FromMinutes(10)
        };
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        // Subscribe to game events
        SubscribeEvents();
    }

    protected override void Added(EntityUid uid, GodComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);

        component.Personality = GodPersonality.GenerateRandom();
        _sawmill.Info($"God personality generated: {string.Join(", ", component.Personality.Traits)}");

        if (component.Announcement is { } locId)
        {
            var announcement = Loc.GetString(locId);
            _chat.DispatchGlobalAnnouncement(announcement, playSound: false, colorOverride: Color.Yellow);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<GodComponent>();

        while (query.MoveNext(out var gamerule, out var comp))
        {
            // Check if we should query the god
            if (Timing.CurTime > comp.NextResponse && _actionLogs.Count > 0)
            {
                comp.NextResponse = Timing.CurTime + comp.ResponseInterval;
                _ = QueryGodAsync(comp);
            }
        }
    }

    private async Task QueryGodAsync(GodComponent component)
    {
        if (_httpClient == null || component.Personality == null) return;

        try
        {
            _sawmill.Debug($"Querying God request...");
            _sawmill.Debug($"Updated logs:\n{string.Join("\n", _actionLogs)}");
            _sawmill.Debug($"Building prompt...");
            var prompt = BuildPrompt(component);

            var request = new
            {
                model = "deepseek/deepseek-r1-0528-qwen3-8b",
                //model = "deepseek-r1-distill-qwen-7b-uncensored_gguf",
                system_prompt = "You will roleplay as a god of this Space Station. Respond with JSON only, no other text. The JSON should have the following structure: {'message': 'Say what you want the residents of the space station to hear', 'action': 'Describe what you are trying to make happen on the station. Be as specific as possible: you can mention events, places or individuals.'}. Fill the message and action fields with your roleplay responses following the instructions.",
                input = prompt,
                temperature = 1,
                max_output_tokens = 5000
            };

            _sawmill.Debug($"Sending request to LMStudio");

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var response = await _httpClient.PostAsJsonAsync("api/v1/chat", request, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                _sawmill.Debug($"Received response from LMStudio");

                // Parse LMStudio response format with the new structure
                var lmResponse = JsonSerializer.Deserialize<LMStudioResponse>(json);

                if (lmResponse?.output != null)
                {
                    // Extract the message content from the output array
                    var messageContent = lmResponse.output
                        .FirstOrDefault(o => o.type == "message")?.content ?? string.Empty;

                    var reasoningContent = lmResponse.output
                        .FirstOrDefault(o => o.type == "reasoning")?.content ?? string.Empty;

                    if (!string.IsNullOrEmpty(reasoningContent))
                    {
                        _sawmill.Debug($"God reasoning: {reasoningContent.Trim()}");
                    }

                    if (!string.IsNullOrEmpty(messageContent))
                    {
                        _sawmill.Debug($"God response: {messageContent.Trim()}");
                        _ = Task.Run(async () => await ProcessGodResponse(messageContent));
                    }
                    else
                    {
                        _sawmill.Warning("No message content found in response");
                    }
                }
            }
            else
            {
                _sawmill.Error($"LMStudio API error: {response.StatusCode}");
            }
        }
        catch (TaskCanceledException)
        {
            _sawmill.Warning("God query timed out - will retry next cycle");
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Error querying god: {ex.Message}");
        }
    }

    private string BuildPrompt(GodComponent component)
    {
        if (component.Personality == null)
            return string.Empty;

        var sb = new StringBuilder();

        //sb.AppendLine($"You are the God of this space station. Your traits: {string.Join(", ", component.Personality.Traits)}");
        sb.AppendLine($"You are the God of this space station.");
        sb.AppendLine($"You like/approve: {string.Join(", ", component.Personality.Likes)}");
        sb.AppendLine($"You dislike/disapprove: {string.Join(", ", component.Personality.Dislikes)}");
        sb.AppendLine($"Your current mood: {component.Personality.Mood:F2}");
        sb.AppendLine();

        sb.AppendLine("Recent events on your station:");
        foreach (var evt in _actionLogs)
        {
            sb.AppendLine($"- {evt}");
        }

        sb.AppendLine();
        sb.AppendLine("Based on these events and your personality, what do you do?");

        _sawmill.Debug($"Sending prompt: {sb.ToString()}");
        return sb.ToString();
    }

    private async Task ProcessGodResponse(string responseText)
    {
        try
        {
            // Try to extract JSON from the response
            var jsonStart = responseText.IndexOf('{');
            var jsonEnd = responseText.LastIndexOf('}') + 1;

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = responseText.Substring(jsonStart, jsonEnd - jsonStart).Replace("\'", "\"");
                var godResponse = JsonSerializer.Deserialize<GodResponse>(json);

                if (godResponse != null)
                {
                    ExecuteGodAction(godResponse);
                    // Clear processed events
                    _actionLogs.Clear();
                }
            }
            else
            {
                _sawmill.Warning($"Could not extract JSON from response: {responseText}");
            }
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Error processing god response: {ex.Message}");
        }
    }

    private void ExecuteGodAction(GodResponse response)
    {
        _sawmill.Info($"God message: {response.Message}");
        _sawmill.Info($"God action: {response.Action}");

        _taskManager.BlockWaitOnTask(Task.Run(() =>
        {
            _chat.DispatchGlobalAnnouncement(response.Message, "The God speaks", colorOverride: Color.Gold);
        }));
    }
}

public sealed class GodPersonality
{
    public List<string> Traits { get; set; } = new();
    public List<string> Likes { get; set; } = new();
    public List<string> Dislikes { get; set; } = new();
    public float Mood { get; set; } = 0.5f;

    private static readonly Random _random = new();
    private static readonly string[] PossibleTraits = { "Benevolent", "Vengeful", "Playful", "Strict", "Chaotic", "Orderly", "Curious", "Apathetic" };
    private static readonly string[] Behaviours =
    {
        "killing animals",
        "fighting other crew members",
        "exploring space",
        "scientific exploration",
        "killing space creatures",
        "making messes",
        "using hostile language",
        "using vulgar language",
        "using friendly language",
        "building structures of worship",
        "building non-religious structures",
        "materialistic wants",
        "helping other crew members",
        "connecting with animals",
        "growing plants",
        "using fire and explosions",
        "love between players",
        "sexual behaviour",
        "generosity",
        "ritual sacrifice",
        "eating meat"
    };

    public static GodPersonality GenerateRandom()
    {
        var behaviours = Behaviours.OrderBy(x => _random.Next()).ToList();
        return new GodPersonality
        {
            Traits = PossibleTraits.OrderBy(x => _random.Next()).Take(_random.Next(1, 4)).ToList(),
            Likes = behaviours.Take(3).ToList(),
            Dislikes = behaviours.Skip(3).Take(3).ToList(),
            Mood = (float)_random.NextDouble()
        };
    }
}


public class LMStudioResponse
{
    public string? model_instance_id { get; set; }
    public List<LMStudioOutput>? output { get; set; }
    public LMStudioStats? stats { get; set; }
    public string? response_id { get; set; }
}

public class LMStudioOutput
{
    public string? type { get; set; } // "reasoning" or "message"
    public string? content { get; set; }
}

public class LMStudioStats
{
    public int input_tokens { get; set; }
    public int total_output_tokens { get; set; }
    public int reasoning_output_tokens { get; set; }
    public double tokens_per_second { get; set; }
    public double time_to_first_token_seconds { get; set; }
}

public class GodResponse
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;
}

