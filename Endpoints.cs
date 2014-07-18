using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using System.Xml.XPath;

namespace LocalHost
{
    public class Endpoints : List<Endpoint>
    {
        public bool TryLoad(Assembly assembly)
        {
            var result = false;
            var resources = assembly.GetManifestResourceNames();
            foreach (var resource in resources
                .Where(resource => resource.EndsWith("endpoints.config", StringComparison.InvariantCultureIgnoreCase)))
            {
                using (var stream = assembly.GetManifestResourceStream(resource))
                {
                    var doc = XDocument.Load(stream);
                    AddRange(doc
                        .XPathSelectElements("Config/Endpoint")
                        .Select(endpoint => new Endpoint
                        {
                            Name = endpoint.Attribute("Name").Value,
                            Address = endpoint.Attribute("Address").Value,
                            Binding = endpoint.Attribute("Binding").Value,
                            Contract = endpoint.Attribute("Contract").Value
                        }));
                }
                result = true;
            }
            return result;
        }
    }
}
