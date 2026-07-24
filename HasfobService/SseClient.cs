using System.Text;
using System.Text.Json;

public class SseClient : IAsyncDisposable
{
    private readonly HttpContext _context;
    private readonly StreamWriter _writer;
    private bool _isClosed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public bool IsConnected => !_isClosed && !_context.RequestAborted.IsCancellationRequested;

    public SseClient(HttpContext context)
    {
        _context = context;

        context.Response.StatusCode = 200;
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        _writer = new StreamWriter(context.Response.Body, Encoding.UTF8, leaveOpen: true);
    }

    public async Task SendAsync(List<SensorDto> sensors)
    {
        if (!IsConnected)
        {
            _isClosed = true;
            return;
        }

        try
        {
            string json = JsonSerializer.Serialize(sensors, JsonOptions);
            await _writer.WriteLineAsync($"data: {json}");
            await _writer.WriteLineAsync();
            await _writer.FlushAsync();
        }
        catch
        {
            _isClosed = true;
        }
    }

    public void Close()
    {
        _isClosed = true;

        try 
        { 
            _context.Abort(); 
        } 
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        _isClosed = true;

        try 
        { 
            await _writer.DisposeAsync(); 
        } 
        catch { }
    }
}