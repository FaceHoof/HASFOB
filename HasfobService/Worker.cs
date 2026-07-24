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

    protected override async Task ExecuteAsync ( CancellationToken stoppingToken )
    {
        _logger.WriteLog ( "HASFOB Service started" );

        HttpClient client = _httpClientFactory.CreateClient ( );
        client.BaseAddress = new Uri ( _config.HomeAssistantBaseUrl );
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue ( "Bearer", _token );
        client.Timeout = TimeSpan.FromSeconds ( 30 );

        await _dataService.InitializeAreasAndDevicesAsync ( client );
        _logger.WriteLog ( "Areas and devices loaded successfully" );

        while ( !stoppingToken.IsCancellationRequested )
        {
            try
            {
                HttpResponseMessage response = await client.GetAsync ( "/api/states", stoppingToken );
                response.EnsureSuccessStatusCode ( );

                string json = await response.Content.ReadAsStringAsync ( stoppingToken );
                using JsonDocument doc = JsonDocument.Parse ( json );

                List<string> logEntries = new List<string> ( );

                foreach ( JsonElement entity in doc.RootElement.EnumerateArray ( ) )
                {
                    string? entityId = entity.GetProperty ( "entity_id" ).GetString ( );
                    if ( string.IsNullOrEmpty ( entityId ) )
                        continue;

                    string? state = entity.GetProperty ( "state" ).GetString ( );
                    string? friendlyName = null;

                    if ( entity.TryGetProperty ( "attributes", out JsonElement attributes ) )
                    {
                        if ( attributes.TryGetProperty ( "friendly_name", out JsonElement fnElement ) )
                            friendlyName = fnElement.GetString ( );
                    }

                    if ( entityId.StartsWith ( "sensor." ) && _dataService.IsAllowed ( entityId ) )
                    {
                        _dataService.UpdateSensor ( entityId, state, friendlyName );
                        string displayName = friendlyName ?? entityId;
                        logEntries.Add ( $"{entityId} - {state} ({displayName})" );
                    }
                    else if ( entityId.StartsWith ( "switch." ) && _dataService.IsSwitchAllowed ( entityId ) )
                    {
                        _dataService.UpdateSwitch ( entityId, state, friendlyName );
                        string displayName = friendlyName ?? entityId;
                        logEntries.Add ( $"{entityId} - {state} ({displayName})" );
                    }
                }

                if ( logEntries.Count > 0 )
                {
                    string logMessage = "New data received:\n" + string.Join ( "\n", logEntries );
                    _logger.WriteLog ( logMessage );
                }
                else
                {
                    _logger.WriteLog ( "New data received: no configured sensors or switches found" );
                }
            }
            catch ( Exception ex )
            {
                _logger.WriteLog ( $"Error fetching data from Home Assistant: {ex.Message}" );
            }

            await Task.Delay ( TimeSpan.FromSeconds ( _config.UpdateIntervalSeconds ), stoppingToken );
        }

        _logger.WriteLog ( "HASFOB Service stopped" );
    }
}