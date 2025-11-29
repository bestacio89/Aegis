using Aegis.Security.Kernel.WCF.Signals;
using System.Xml;

namespace Aegis.Security.Kernel.WCF.Probes;

public sealed class WcfSecurityProbe
{
    public WcfSecuritySignal ProbeFromConfig(string configPath)
    {
        try
        {
            var doc = new XmlDocument();
            doc.Load(configPath);

            var bindings = doc.GetElementsByTagName("bindings");
            if (bindings.Count == 0)
                return new WcfSecuritySignal(
                    "Unknown", "Unknown", false, null, null, null, false, false, null);

            var serviceName = Path.GetFileNameWithoutExtension(configPath);

            string bindingName = "Unknown";
            string? securityMode = null;
            string? clientCredentialType = null;
            string? algorithmSuite = null;
            bool isHttps = false;
            bool allowsAnonymous = true;

            if (bindings[0] is not XmlNode bindingsNode)
                return new WcfSecuritySignal(
                    "Unknown", "Unknown", false, null, null, null, false, false, null);

            foreach (XmlNode b in bindingsNode.ChildNodes)
            {
                bindingName = b.Name;

                var secNode = b.SelectSingleNode("security");
                if (secNode != null)
                {
                    securityMode = secNode.Attributes?["mode"]?.Value;

                    var messageNode = secNode.SelectSingleNode("message");
                    algorithmSuite = messageNode?.Attributes?["algorithmSuite"]?.Value;

                    var transportNode = secNode.SelectSingleNode("transport");
                    clientCredentialType = transportNode?.Attributes?["clientCredentialType"]?.Value;
                }

                var transport = b.SelectSingleNode("transport");
                if (transport?.Attributes?["scheme"]?.Value == "https")
                    isHttps = true;

                var creds = b.SelectSingleNode("message");
                if (creds?.Attributes?["clientCredentialType"]?.Value == "None")
                    allowsAnonymous = true;
            }

            bool metadataExposed = doc.GetElementsByTagName("serviceMetadata").Count > 0;

            return new WcfSecuritySignal(
                serviceName,
                bindingName,
                isHttps,
                securityMode,
                clientCredentialType,
                algorithmSuite,
                metadataExposed,
                allowsAnonymous,
                null
            );
        }
        catch (Exception ex)
        {
            return new WcfSecuritySignal(
                "Unknown", "Unknown", false, null, null, null, false, false, ex.Message
            );
        }
    }
}
