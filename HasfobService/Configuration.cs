using System.Xml.Serialization;

[XmlRoot("configuration")]
public class Configuration
{
    [XmlElement("homeAssistantServer")]
    public string HomeAssistantServer { get; set; } = string.Empty;

    [XmlElement("homeAssistantPort")]
    public int HomeAssistantPort { get; set; }

    [XmlElement("homeAssistantTokenFile")]
    public string HomeAssistantTokenFile { get; set; } = string.Empty;

    [XmlElement("servicePort")]
    public int ServicePort { get; set; }

    [XmlArray("sensors")]
    [XmlArrayItem("sensor")]
    public List<SensorConfig> Sensors { get; set; } = new List<SensorConfig>();

    [XmlElement("updateIntervalSeconds")]
    public int UpdateIntervalSeconds { get; set; }

    [XmlElement("logRetentionDays")]
    public int LogRetentionDays { get; set; }

    [XmlElement("showAreaNames")]
    public string ShowAreaNames { get; set; } = "Y";

    [XmlElement("showDeviceNames")]
    public string ShowDeviceNames { get; set; } = "Y";

    [XmlIgnore]
    public bool ShowAreas => ShowAreaNames?.Trim().ToUpper() == "Y";

    [XmlIgnore]
    public bool ShowDevices => ShowDeviceNames?.Trim().ToUpper() == "Y";

    [XmlArray ( "switches" )]
    [XmlArrayItem ( "switch" )]
    public List<SwitchConfig> Switches { get; set; } = new ( );

    [XmlIgnore]
    public string HomeAssistantBaseUrl
    {
        get
        {
            string server = HomeAssistantServer.TrimEnd('/');
            return $"{server}:{HomeAssistantPort}/";
        }
    }

    public string? EntityNamesXmlPath { get; set; } = "entity_names.xml";
}

public class SensorConfig
{
    [XmlElement("entityId")]
    public string EntityId { get; set; } = string.Empty;

    [XmlElement("friendlyName")]
    public string? FriendlyName { get; set; }

    [XmlElement("decimalPlaces")]
    public int? DecimalPlaces { get; set; }
}

public class SwitchConfig
{
    [XmlElement ( "entityId" )]
    public string EntityId { get; set; } = string.Empty;

    [XmlElement ( "friendlyName" )]
    public string? FriendlyName { get; set; }
}