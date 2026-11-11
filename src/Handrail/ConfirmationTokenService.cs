using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Handrail;

/// <summary>Why a confirmation token was refused.</summary>
public enum ConfirmationFailure
{
    None,
    Missing,
    Malformed,
    BadSignature,
    Expired,
    WrongRequest,
    AlreadyUsed,
}

/// <summary>
/// One-time confirmation tokens: an HMAC over tool, argument hash, user and expiry, plus a random nonce that can be
/// spent once. The dialog mints one after a trusted click; the invoke endpoint spends it. The nonce store is in
/// memory, so a multi-instance deployment needs one shared key and sticky routing (docs/security.md).
/// </summary>
public sealed class ConfirmationTokenService
{
    private readonly byte[] _key;
    private readonly TimeProvider _time;
    private readonly TimeSpan _lifetime;
    private readonly ConcurrentDictionary<string, long> _spent = new();

    public ConfirmationTokenService(byte[]? key = null, TimeProvider? time = null, TimeSpan? lifetime = null)
    {
        _key = key ?? RandomNumberGenerator.GetBytes(32);
        if (_key.Length < 16)
        {
            throw new ArgumentException("The confirmation key must be at least 16 bytes.", nameof(key));
        }

        _time = time ?? TimeProvider.System;
        _lifetime = lifetime ?? TimeSpan.FromSeconds(60);
    }

    public string Mint(string tool, string argumentsHash, string userId)
    {
        var nonce = Base64Url(RandomNumberGenerator.GetBytes(16));
        var expires = _time.GetUtcNow().Add(_lifetime).ToUnixTimeSeconds();
        var payload = $"{nonce}.{expires}";
        return payload + "." + Sign(payload, tool, argumentsHash, userId);
    }

    /// <summary>Checks the token and spends it. A second call with the same token fails.</summary>
    public ConfirmationFailure TryConsume(string? token, string tool, string argumentsHash, string userId)
    {
        if (string.IsNullOrEmpty(token))
        {
            return ConfirmationFailure.Missing;
        }

        var parts = token.Split('.');
        if (parts.Length != 3 || !long.TryParse(parts[1], out var expires))
        {
            return ConfirmationFailure.Malformed;
        }

        var payload = parts[0] + "." + parts[1];
        var expected = Sign(payload, tool, argumentsHash, userId);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[2])))
        {
            return ConfirmationFailure.BadSignature;
        }

        var now = _time.GetUtcNow().ToUnixTimeSeconds();
        if (expires < now)
        {
            return ConfirmationFailure.Expired;
        }

        Sweep(now);
        return _spent.TryAdd(parts[0], expires) ? ConfirmationFailure.None : ConfirmationFailure.AlreadyUsed;
    }

    private void Sweep(long now)
    {
        foreach (var entry in _spent)
        {
            if (entry.Value < now)
            {
                _spent.TryRemove(entry.Key, out _);
            }
        }
    }

    private string Sign(string payload, string tool, string argumentsHash, string userId)
    {
        var data = Encoding.UTF8.GetBytes($"{payload}\n{tool}\n{argumentsHash}\n{userId}");
        return Base64Url(HMACSHA256.HashData(_key, data));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>SHA-256 of the arguments in canonical form (members sorted, no whitespace), so mint and invoke agree.</summary>
    public static string HashArguments(JsonElement arguments)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, arguments);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
