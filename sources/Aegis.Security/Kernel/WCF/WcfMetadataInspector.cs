using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.ServiceModel.Channels;
using System.Web.Services.Description;
using Binding = System.Web.Services.Description.Binding;
namespace Aegis.Security.Kernel.WCF;

public sealed class WcfMetadataInspector
{
    public async Task<WcfMetadataInfo> InspectAsync(string serviceUrl)
    {
        try
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            // 1. Download the WSDL document directly
            var wsdlText = await http.GetStringAsync(serviceUrl);
            using var reader = new StringReader(wsdlText);
            using var xml = new System.Xml.XmlTextReader(reader);

            // 2. Parse using .NET's WSDL 1.1 Description parser
            var serviceDescription = ServiceDescription.Read(xml);

            // 3. Extract the bindings
            var bindings = serviceDescription.Bindings
                .Cast<Binding>()
                .Select(b => b.Name)
                .Distinct()
                .ToArray();

            return new WcfMetadataInfo(serviceUrl, bindings);
        }
        catch (Exception ex)
        {
            return new WcfMetadataInfo(
                ServiceUrl: serviceUrl,
                Bindings: Array.Empty<string>(),
                Error: ex.Message
            );
        }
    }
}

public sealed record WcfMetadataInfo(
    string ServiceUrl,
    string[] Bindings,
    string? Error = null
);
