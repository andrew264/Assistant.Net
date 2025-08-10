using Assistant.Net.Configuration;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Assistant.Net.Services.Core;

public class ConfigService
{
    private const string ConfigPath = "Configuration/config.yaml";
    private readonly ILogger<ConfigService> _logger;

    public ConfigService(ILogger<ConfigService> logger)
    {
        _logger = logger;
        Config = LoadConfig();
    }

    public Config Config { get; private set; }

    private Config LoadConfig()
    {
        if (!File.Exists(ConfigPath))
        {
            _logger.LogCritical("Config file not found at {Path}. Exiting.", ConfigPath);
            // In a real app, you might generate a default or handle this differently
            throw new FileNotFoundException("Configuration file not found.", ConfigPath);
        }

        try
        {
            var yamlContent = File.ReadAllText(ConfigPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance) // Assuming yaml keys are camelCase
                .IgnoreUnmatchedProperties() // Be lenient if extra keys exist
                .Build();

            var config = deserializer.Deserialize<Config>(yamlContent);

            // Basic validation
            if (string.IsNullOrWhiteSpace(config.Client.Token))
            {
                _logger.LogCritical("Bot token is missing in the configuration.");
                throw new InvalidOperationException("Bot token is missing in config.yaml.");
            }

            if (config.Client.OwnerId is null or 0)
                _logger.LogWarning("OwnerId is missing or zero in the configuration.");
            // Depending on requirements, you might throw or just warn

            _logger.LogInformation("Configuration loaded successfully.");
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to load or parse configuration file: {Path}", ConfigPath);
            throw; // Re-throw to stop the application
        }
    }
}