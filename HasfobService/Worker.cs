using System.Net.Http.Headers;
using System.Text.Json;

public class Worker : BackgroundService
{
    private readonly TokenReader _tokenReader;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SensorDataService _dataService;
    private readonly Logger _logger;
    private readonly Configuration _config;
    private readonly string? _token;

    public Worker ( IHttpClientFactory httpClientFactory, SensorDataService dataService, TokenReader tokenReader, Logger logger, Configuration config )
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _dataService = dataService;
        _tokenReader = tokenReader;
        _config = config;

        try
        {
            _token = _tokenReader.GetToken ( _config.HomeAssistantTokenFile );
            if ( string.IsNullOrEmpty ( _token ) )
                throw new InvalidDataException ( "Token is empty" );
        }
        catch ( Exception ex )
        {
            _logger.WriteLog ( $"Token reading error: {ex.Message}" );
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.WriteLog("HASFOB Service started");

        HttpClient client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_config.HomeAssistantBaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        client.Timeout = TimeSpan.FromSeconds(10);

        try
        {
            await _dataService.InitializeAreasAndDevicesAsync(client, stoppingToken);
            _logger.WriteLog("Areas and devices loaded successfully");
        }
        catch (OperationCanceledException)
        {
            _logger.WriteLog("Service cancellation requested during initialization.");
            return;
        }
        catch (Exception ex)
        {
            _logger.WriteLog($"Initialization error: {ex.Message}");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                List<string> logEntries = new List<string>();

                foreach (SensorConfig sensorConfig in _config.Sensors)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    await FetchAndProcessEntityAsync(client, sensorConfig.EntityId, isSensor: true, logEntries, stoppingToken);
                }

                if (_config.Switches != null)
                {
                    foreach (var switchConfig in _config.Switches)
                    {
                        stoppingToken.ThrowIfCancellationRequested();

                        await FetchAndProcessEntityAsync(client, switchConfig.EntityId, isSensor: false, logEntries, stoppingToken);
                    }
                }

                if (logEntries.Count > 0)
                {
                    string logMessage = $"New data received ({logEntries.Count} configured entities):\n" + string.Join("\n", logEntries);
                    _logger.WriteLog(logMessage);
                }
                else
                {
                    _logger.WriteLog("New data received: no configured sensors or switches found");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.WriteLog($"Error fetching data from Home Assistant: {ex.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_config.UpdateIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.WriteLog("HASFOB Service stopped");
    }

    private async Task FetchAndProcessEntityAsync(HttpClient client, string entityId, bool isSensor, List<string> logEntries, CancellationToken stoppingToken)
    {
        try
        {
            HttpResponseMessage response = await client.GetAsync($"/api/states/{entityId}", stoppingToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.WriteLog($"Warning: Entity {entityId} not found in Home Assistant");
                return;
            }

            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(stoppingToken);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string? state = root.GetProperty("state").GetString();
            string? friendlyName = null;

            if (root.TryGetProperty("attributes", out JsonElement attributes))
            {
                if (attributes.TryGetProperty("friendly_name", out JsonElement fnElement))
                    friendlyName = fnElement.GetString();
            }

            if (isSensor)
            {
                _dataService.UpdateSensor(entityId, state, friendlyName);
            }
            else
            {
                _dataService.UpdateSwitch(entityId, state, friendlyName);
            }

            string displayName = friendlyName ?? entityId;
            logEntries.Add($"{entityId} - {state} ({displayName})");
        }
        catch (Exception ex)
        {
            _logger.WriteLog($"Failed to update {entityId}: {ex.Message}");
        }
    }
}