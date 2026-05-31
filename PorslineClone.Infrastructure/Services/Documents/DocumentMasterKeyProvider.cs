using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PorslineClone.Infrastructure.Options;

namespace PorslineClone.Infrastructure.Services.Documents;

/// <summary>KEK (Master Key) از متغیرهای محیطی — پشتیبانی چند نسخه برای چرخش.</summary>
public sealed class DocumentMasterKeyProvider
{
    private readonly FrozenDictionary<string, byte[]> _keysById;
    private readonly string _primaryKeyId;
    private readonly bool _encryptionRequested;

    public DocumentMasterKeyProvider(
        IConfiguration configuration,
        IOptions<DocumentEncryptionOptions> options,
        ILogger<DocumentMasterKeyProvider> logger)
    {
        var opts = options.Value;
        _encryptionRequested = opts.Enabled;
        _primaryKeyId = string.IsNullOrWhiteSpace(opts.PrimaryKeyId) ? "v1" : opts.PrimaryKeyId.Trim();

        var keys = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        LoadFromEnvironment(keys, configuration, logger);
        _keysById = keys.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        if (_encryptionRequested && !_keysById.ContainsKey(_primaryKeyId))
        {
            throw new InvalidOperationException(
                $"Document encryption is enabled but primary key '{_primaryKeyId}' is missing. " +
                "Set DOCUMENT_ENCRYPTION_MASTER_KEY or DOCUMENT_ENCRYPTION_KEYS_JSON.");
        }

        if (_encryptionRequested)
            logger.LogInformation("Document encryption enabled. Primary KEK id={KeyId}, available keys={Count}", _primaryKeyId, _keysById.Count);
    }

    public bool EncryptionEnabled => _encryptionRequested && _keysById.Count > 0;

    public string PrimaryKeyId => _primaryKeyId;

    public byte[] GetKey(string keyId)
    {
        if (!_keysById.TryGetValue(keyId, out var key))
            throw new KeyNotFoundException($"Master encryption key '{keyId}' is not configured.");
        return key;
    }

    public byte[] GetPrimaryKey() => GetKey(_primaryKeyId);

    public IReadOnlyCollection<string> KeyIds => _keysById.Keys;

    private static void LoadFromEnvironment(
        Dictionary<string, byte[]> keys,
        IConfiguration configuration,
        ILogger logger)
    {
        var json = configuration["DOCUMENT_ENCRYPTION_KEYS_JSON"]?.Trim();
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (map is not null)
                {
                    foreach (var (id, b64) in map)
                        TryAddKey(keys, id, b64, logger);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to parse DOCUMENT_ENCRYPTION_KEYS_JSON");
            }
        }

        var legacy = configuration["DOCUMENT_ENCRYPTION_MASTER_KEY"]?.Trim();
        if (!string.IsNullOrEmpty(legacy))
            TryAddKey(keys, "v1", legacy, logger);

        foreach (var child in configuration.GetChildren())
        {
            if (!child.Key.StartsWith("DOCUMENT_ENCRYPTION_KEY_", StringComparison.OrdinalIgnoreCase))
                continue;
            var id = child.Key["DOCUMENT_ENCRYPTION_KEY_".Length..];
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(child.Value))
                TryAddKey(keys, id, child.Value, logger);
        }
    }

    private static void TryAddKey(Dictionary<string, byte[]> keys, string keyId, string base64, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(base64))
            return;

        try
        {
            var raw = Convert.FromBase64String(base64.Trim());
            if (raw.Length != 32)
            {
                logger.LogWarning("Master key {KeyId} must be 32 bytes (256-bit) when Base64-decoded; got {Length}", keyId, raw.Length);
                return;
            }

            keys[keyId.Trim()] = raw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invalid Base64 master key for id {KeyId}", keyId);
        }
    }
}
