using System.Net;

namespace FlowDisk.Api.Attachments;

public sealed class ClamAvAttachmentScannerOptions(
    IPAddress address,
    int port,
    TimeSpan timeout)
{
    public IPAddress Address { get; } = address;
    public int Port { get; } = port;
    public TimeSpan Timeout { get; } = timeout;

    public static ClamAvAttachmentScannerOptions FromConfiguration(
        IConfiguration configuration)
    {
        var host = configuration["Attachments:Scanner:PrivateIpAddress"];
        if (!IPAddress.TryParse(host, out var address) ||
            !IsPrivateAddress(address))
        {
            throw new InvalidOperationException(
                "The ClamAV scanner must use a configured private IP address.");
        }

        var port = ReadInteger(
            configuration,
            "Attachments:Scanner:Port",
            3310,
            1,
            65535);
        var timeoutSeconds = ReadInteger(
            configuration,
            "Attachments:Scanner:TimeoutSeconds",
            30,
            1,
            120);
        return new ClamAvAttachmentScannerOptions(
            address,
            port,
            TimeSpan.FromSeconds(timeoutSeconds));
    }

    private static int ReadInteger(
        IConfiguration configuration,
        string key,
        int defaultValue,
        int minimum,
        int maximum)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
        {
            return defaultValue;
        }

        if (!int.TryParse(configuration[key], out var value) ||
            value < minimum ||
            value > maximum)
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' must be between {minimum} and {maximum}.");
        }

        return value;
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return false;
            }

            return bytes[0] == 10 ||
                (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168);
        }

        return address.AddressFamily ==
                System.Net.Sockets.AddressFamily.InterNetworkV6 &&
            (bytes[0] & 0xfe) == 0xfc;
    }
}
