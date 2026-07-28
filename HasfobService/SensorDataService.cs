using System.Text;
using System.Text.Json;

public record SensorInfo
{
    public string State { get; set; } = "unknown";
    public string? FriendlyName { get; set; }
    public string? AreaName { get; set; }
    public string? DeviceName { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public int? DecimalPlaces { get; set; }
}

public record SwitchInfo
{
    public bool IsOn { get; set; } = false;
    public string? FriendlyName { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public record SensorDto
{
    public string EntityId { get; set; } = string.Empty;
    public string? FriendlyName { get; set; }
    public string? AreaName { get; set; }
    public string? DeviceName { get; set; }
    public string State { get; set; } = string.Empty;
    public string Type { get; set; } = "Default";
    public double Min { get; set; } = 0;
    public double Max { get; set; } = 100;
}

public record SwitchDto
{
    public string EntityId { get; set; } = string.Empty;
    public string? FriendlyName { get; set; }
    public bool IsOn { get; set; }
}

public record AllDataDto
{
    public List<SensorDto> Sensors { get; set; } = new ( );
    public List<SwitchDto> Switches { get; set; } = new ( );
}

public class SensorDataService
{
    private readonly Configuration _config;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly Logger _logger;
    private readonly Dictionary<string, SensorConfig> _sensorConfigs;
    private readonly Dictionary<string, SensorInfo> _sensors = new ( StringComparer.OrdinalIgnoreCase );
    private readonly Dictionary<string, SwitchInfo> _switchStates = new ( StringComparer.OrdinalIgnoreCase );
    private readonly object _lock = new ( );
    private readonly List<SseClient> _clients = new ( );
    private readonly object _clientsLock = new ( );
    private readonly Dictionary<string, string> _entityToDevice = new ( StringComparer.OrdinalIgnoreCase );
    private readonly Dictionary<string, string> _entityToArea = new ( StringComparer.OrdinalIgnoreCase );
    private readonly List<SwitchConfig> _switches;
    private readonly string _htmlTemplate;

    public SensorDataService (Configuration config, IHostApplicationLifetime lifetime, Logger logger)
    {
        _config = config;
        _lifetime = lifetime;
        _logger = logger;

        _sensorConfigs = config.Sensors.ToDictionary (
            s => s.EntityId,
            s => s,
            StringComparer.OrdinalIgnoreCase );

        _switches = config.Switches ?? new List<SwitchConfig> ( );

        try
        {
            _htmlTemplate = LoadHtmlTemplate ( );
            _logger.WriteLog ( "HTML template loaded successfully" );
        }
        catch ( Exception ex )
        {
            _logger.WriteLog ( $"CRITICAL ERROR: Failed to load WebPage.html template: {ex.Message}" );
            _htmlTemplate = "<h1 style='color:red'>Failed to load WebPage.html</h1>";
        }

        _lifetime.ApplicationStopping.Register ( CloseAllClients );
    }

    private string LoadHtmlTemplate ( )
    {
        string baseDirPath = Path.Combine ( AppContext.BaseDirectory, "WebPage.html" );
        if ( File.Exists ( baseDirPath ) )
            return File.ReadAllText ( baseDirPath, Encoding.UTF8 );

        string devPath = Path.Combine ( Directory.GetCurrentDirectory ( ), "WebPage.html" );
        if ( File.Exists ( devPath ) )
            return File.ReadAllText ( devPath, Encoding.UTF8 );

        throw new FileNotFoundException ( "WebPage.html not found." );
    }

    public async Task InitializeAreasAndDevicesAsync(HttpClient client, CancellationToken cancellationToken = default)
    {
        await InitializeAreasAsync(client, cancellationToken);
        await InitializeDevicesAsync(client, cancellationToken);
    }

    private async Task InitializeAreasAsync(HttpClient client, CancellationToken cancellationToken)
    {
        if (!_config.ShowAreas) return;
        try
        {
            var payload = new
            {
                template = """
                {% set ns = namespace(result={}) %}
                {% for state in states.sensor %}
                    {% set area = area_name(state.entity_id) %}
                    {% if area and area != 'None' and area != 'null' %}
                        {% set ns.result = dict(ns.result, **{state.entity_id: area}) %}
                    {% endif %}
                {% endfor %}
                {{ ns.result | tojson }}
                """
            };

            HttpResponseMessage response = await client.PostAsJsonAsync("/api/template", payload, cancellationToken);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var areas = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (areas?.Count > 0)
            {
                lock (_lock)
                {
                    _entityToArea.Clear();
                    foreach (var kv in areas)
                    {
                        if (!string.IsNullOrWhiteSpace(kv.Value))
                            _entityToArea[kv.Key] = kv.Value.Trim();
                    }
                }
                _logger.WriteLog($"Loaded areas for {areas.Count} sensors");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.WriteLog($"Warning: Failed to initialize areas: {ex.Message}");
        }
    }

    private async Task InitializeDevicesAsync(HttpClient client, CancellationToken cancellationToken)
    {
        if (!_config.ShowDevices) return;
        try
        {
            var payload = new
            {
                template = """
                {% set ns = namespace(result={}) %}
                {% for state in states.sensor %}
                    {% set device = device_name(state.entity_id) %}
                    {% if device and device != 'None' and device != 'null' %}
                        {% set ns.result = dict(ns.result, **{state.entity_id: device}) %}
                    {% endif %}
                {% endfor %}
                {{ ns.result | tojson }}
                """
            };

            HttpResponseMessage response = await client.PostAsJsonAsync("/api/template", payload, cancellationToken);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var devices = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (devices?.Count > 0)
            {
                lock (_lock)
                {
                    _entityToDevice.Clear();
                    foreach (var kv in devices)
                    {
                        if (!string.IsNullOrWhiteSpace(kv.Value))
                            _entityToDevice[kv.Key] = kv.Value.Trim();
                    }
                }
                _logger.WriteLog($"Loaded devices for {devices.Count} sensors");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.WriteLog($"Warning: Failed to initialize devices: {ex.Message}");
        }
    }

    public bool IsAllowed ( string entityId ) => _sensorConfigs.ContainsKey ( entityId );

    public bool IsSwitchAllowed ( string entityId )
    {
        return _switches.Any ( s => string.Equals ( s.EntityId, entityId, StringComparison.OrdinalIgnoreCase ) );
    }

    public void UpdateSensor ( string entityId, string? rawState, string? haFriendlyName )
    {
        if ( !IsAllowed ( entityId ) ) 
            return;

        SensorConfig sensorConfig = _sensorConfigs [ entityId ];
        string? finalFriendlyName = sensorConfig.FriendlyName ?? haFriendlyName;
        string? areaName = null;
        string? deviceName = null;

        if ( _config.ShowAreas )
            _entityToArea.TryGetValue ( entityId, out areaName );
        if ( _config.ShowDevices )
            _entityToDevice.TryGetValue ( entityId, out deviceName );

        string formattedState = FormatState ( rawState, sensorConfig.DecimalPlaces );

        bool shouldNotify = false;
        lock ( _lock )
        {
            SensorInfo newInfo = new SensorInfo
            {
                State = formattedState,
                FriendlyName = finalFriendlyName,
                AreaName = areaName,
                DeviceName = deviceName,
                DecimalPlaces = sensorConfig.DecimalPlaces,
                LastUpdated = DateTime.UtcNow
            };

            if ( !_sensors.TryGetValue ( entityId, out var existing ) ||
                existing.State != newInfo.State ||
                existing.FriendlyName != newInfo.FriendlyName ||
                existing.AreaName != newInfo.AreaName ||
                existing.DeviceName != newInfo.DeviceName )
            {
                shouldNotify = true;
            }

            _sensors [ entityId ] = newInfo;
        }

        if ( shouldNotify )
            NotifyAllClients ( );
    }

    public void UpdateSwitch ( string entityId, string? rawState, string? haFriendlyName )
    {
        if ( !IsSwitchAllowed ( entityId ) ) 
            return;

        bool isOn = string.Equals ( rawState?.Trim ( ), "on", StringComparison.OrdinalIgnoreCase );

        lock ( _lock )
        {
            SwitchInfo switchInfo = new SwitchInfo
            {
                IsOn = isOn,
                FriendlyName = haFriendlyName,
                LastUpdated = DateTime.UtcNow
            };

            _switchStates [ entityId ] = switchInfo;

            if ( !_sensors.ContainsKey ( entityId ) )
            {
                _sensors [ entityId ] = new SensorInfo
                {
                    State = isOn ? "on" : "off",
                    FriendlyName = haFriendlyName
                };
            }
        }

        NotifyAllClients ( );
    }

    private string FormatState ( string? state, int? decimalPlaces )
    {
        if ( string.IsNullOrWhiteSpace ( state ) )
            return "—";

        if ( !double.TryParse ( state, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double value ) )
        {
            return state;
        }

        if ( decimalPlaces.HasValue )
        {
            double rounded = Math.Round ( value, decimalPlaces.Value );
            return rounded.ToString ( $"F{decimalPlaces.Value}", System.Globalization.CultureInfo.InvariantCulture );
        }

        return value.ToString ( System.Globalization.CultureInfo.InvariantCulture );
    }

    private void NotifyAllClients ( )
    {
        List<SensorDto> currentData = GetAllSensors ( );
        lock ( _clientsLock )
        {
            _clients.RemoveAll ( c => !c.IsConnected );
            foreach ( SseClient client in _clients.ToList ( ) )
            {
                _ = client.SendAsync ( currentData );
            }
        }
    }

    public SseClient AddClient ( HttpContext context )
    {
        SseClient client = new SseClient ( context );
        lock ( _clientsLock ) 
            _clients.Add ( client );
        return client;
    }

    public void RemoveClient ( SseClient client )
    {
        lock ( _clientsLock ) 
            _clients.Remove ( client );
    }

    private void CloseAllClients ( )
    {
        lock ( _clientsLock )
        {
            foreach ( var client in _clients.ToList ( ) )
                client.Close ( );
            _clients.Clear ( );
        }
    }

    public List<SensorDto> GetAllSensors()
    {
        lock (_lock)
        {
            return _sensors.Select(kv =>
            {
                _sensorConfigs.TryGetValue(kv.Key, out var config);

                return new SensorDto
                {
                    EntityId = kv.Key,
                    FriendlyName = kv.Value.FriendlyName,
                    AreaName = kv.Value.AreaName,
                    DeviceName = kv.Value.DeviceName,
                    State = kv.Value.State,
                    Type = (config?.Type ?? SensorType.Default).ToString(),
                    Min = config?.MinValue ?? 0,
                    Max = config?.MaxValue ?? 100
                };
            })
            .OrderBy(x => x.AreaName ?? "")
            .ThenBy(x => x.DeviceName ?? "")
            .ThenBy(x => x.FriendlyName ?? x.EntityId)
            .ToList();
        }
    }

    public List<SwitchDto> GetAllSwitches ( )
    {
        List<SwitchDto> result = new List<SwitchDto> ( );

        foreach ( SwitchConfig config in _switches )
        {
            bool isOn = false;
            string? friendlyName = config.FriendlyName;

            if ( _switchStates.TryGetValue ( config.EntityId, out var swInfo ) )
            {
                isOn = swInfo.IsOn;
                //If friendlyName is missing from the config, we take it from HA.
                if ( string.IsNullOrEmpty ( friendlyName ) && !string.IsNullOrEmpty ( swInfo.FriendlyName ) )
                    friendlyName = swInfo.FriendlyName;
            }
            else if ( _sensors.TryGetValue ( config.EntityId, out SensorInfo? sensorInfo ) )
            {
                isOn = sensorInfo.State?.Trim ( ).ToLower ( ) == "on";
                if ( string.IsNullOrEmpty ( friendlyName ) && !string.IsNullOrEmpty ( sensorInfo.FriendlyName ) )
                    friendlyName = sensorInfo.FriendlyName;
            }

            result.Add ( new SwitchDto
            {
                EntityId = config.EntityId,
                FriendlyName = friendlyName,
                IsOn = isOn
            } );
        }

        return result.OrderBy ( x => x.FriendlyName ?? x.EntityId ).ToList ( );
    }

    public AllDataDto GetAllData ( )
    {
        return new AllDataDto
        {
            Sensors = GetAllSensors ( ),
            Switches = GetAllSwitches ( )
        };
    }

    public string GetHtmlPage ( )
    {
        return _htmlTemplate
            .Replace ( "{{SHOW_AREAS}}", _config.ShowAreas ? "true" : "false" )
            .Replace ( "{{SHOW_DEVICES}}", _config.ShowDevices ? "true" : "false" )
            .Replace ( "{{SHOW_SWITCHES}}", ( _switches.Count > 0 ).ToString ( ).ToLowerInvariant ( ) );
    }
}