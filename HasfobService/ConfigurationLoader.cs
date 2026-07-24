using System.Xml.Serialization;

public static class ConfigurationLoader
{
    private static readonly string DefaultConfigPath = Path.Combine(AppContext.BaseDirectory, "configuration.xml");

    public static Configuration Load(string? filePath = null)
    {
        string path = filePath ?? DefaultConfigPath;
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Configuration file not found: {path}");
        }

        try
        {
            XmlSerializer serializer = new XmlSerializer(typeof(Configuration));
            using StreamReader reader = new StreamReader(path);
            Configuration? config = (Configuration?)serializer.Deserialize(reader);

            if (config == null)
                throw new Exception("Config is empty");

            ValidateConfiguration(config);
            return config;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load configuration from {path}", ex);
        }
    }

    private static void ValidateConfiguration(Configuration? config)
    {
        if (string.IsNullOrWhiteSpace(config?.HomeAssistantServer))
            throw new InvalidDataException("HomeAssistantServer is not specified.");

        if (config.HomeAssistantPort <= 0)
            throw new InvalidDataException("HomeAssistantPort must be greater than 0.");

        if (string.IsNullOrWhiteSpace(config.HomeAssistantTokenFile))
            throw new InvalidDataException("HomeAssistantTokenFile path is not specified.");

        if (config.ServicePort <= 0)
            throw new InvalidDataException("ServicePort must be greater than 0.");

        if (config.Sensors == null || config.Sensors.Count == 0)
            throw new InvalidDataException("At least one sensor must be configured.");

        if (config.UpdateIntervalSeconds < 10)
            throw new InvalidDataException("UpdateIntervalSeconds should be at least 10 seconds.");

        //Check new sensors
        foreach (SensorConfig sensor in config.Sensors)
        {
            if (string.IsNullOrWhiteSpace(sensor.EntityId))
                throw new InvalidDataException("Sensor entityId cannot be empty.");
        }
    }
}