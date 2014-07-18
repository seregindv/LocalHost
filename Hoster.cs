using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.Text;
using System.Reflection;
using System.ServiceModel;

namespace LocalHost
{
    public class Hoster
    {
        #region private fields

        readonly AssemblyResolver _resolver;
        string[] _contracts;
        string[] _services;
        readonly Endpoints _endpoints;
        readonly Dictionary<Type, ServiceHost> _hosts;

        #endregion

        #region .ctor

        private Hoster()
        {
            _endpoints = new Endpoints();
            _resolver = new AssemblyResolver();
            _hosts = new Dictionary<Type, ServiceHost>();
        }

        #endregion

        #region factory methods

        public static Hoster CreateFolderHost(string folder)
        {
            var result = new Hoster
            {
                _contracts = Directory.EnumerateFiles(folder, "*.contracts.dll").ToArray()
            };
            Trace.WriteLine("Contract assembly candidates\n" + result._contracts.Aggregate(new StringBuilder(), (sb, s) => sb.AppendLine("  " + s), sb => sb.ToString()));
            result._services = Directory.EnumerateFiles(folder, "*.services.dll").ToArray();
            Trace.WriteLine("Service assembly candidates\n" + result._services.Aggregate(new StringBuilder(), (sb, s) => sb.AppendLine("  " + s), sb => sb.ToString()));
            result._resolver.AddFolder(folder);
            result._resolver.Connect();
            result.LoadEndpoints();
            result.ConstructHosts();
            return result;
        }

        public static Hoster CreateContractHost(string contractFile)
        {
            var result = new Hoster
            {
                _contracts = new[] { contractFile }
            };
            Trace.WriteLine("Contract assembly\n  " + contractFile);
            var folder = GetFolder(contractFile);
            result._services = Directory.EnumerateFiles(folder, "*.services.dll").ToArray();
            Trace.WriteLine("Service assembly candidates\n" + result._services.Aggregate(new StringBuilder(), (sb, s) => sb.AppendLine("  " + s), sb => sb.ToString()));
            result._resolver.AddFolder(folder);
            result._resolver.Connect();
            result.LoadEndpoints();
            result.ConstructHosts();
            return result;
        }

        public static Hoster CreateContractAndServiceHost(string contractFile, string[] serviceFiles)
        {
            var result = new Hoster
            {
                _contracts = new[] { contractFile }
            };
            Trace.WriteLine("Contract assembly\n  " + contractFile);
            var folder = GetFolder(contractFile);
            result._services = serviceFiles;
            Trace.WriteLine("Service assemblies\n" + result._services.Aggregate(new StringBuilder(), (sb, s) => sb.AppendLine("  " + s), sb => sb.ToString()));
            result._resolver.AddFolder(folder);
            result._resolver.Connect();
            result.LoadEndpoints();
            result.ConstructHosts();
            return result;
        }

        #endregion

        #region private

        static string GetFolder(string file)
        {
            var result = Path.GetDirectoryName(file);
            if (String.IsNullOrEmpty(result))
                result = Assembly.GetExecutingAssembly().Location;
            return result;
        }

        void LoadEndpoints()
        {
            foreach (var contract in _contracts)
                _endpoints.TryLoad(Assembly.ReflectionOnlyLoadFrom(contract));
        }

        Assembly[] LoadServices()
        {
            return _services.Select(Assembly.LoadFrom).ToArray();
        }

        void ConstructHosts()
        {
            var services = LoadServices();
            foreach (var endpoint in _endpoints.Where(ep => !ep.Address.StartsWith("http")))
            {
                var contractType = Type.GetType(endpoint.Contract);
                if (contractType == null)
                {
                    Trace.WriteLine("Unable to get type for " + endpoint.Contract);
                    continue;
                }
                var serviceTypes = services
                    .Select(service => service.GetTypes()
                        .Where(t => contractType.IsAssignableFrom(t) && t.IsClass))
                    .SelectMany(types => types).ToArray();
                if (serviceTypes.Length > 1)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Trace.WriteLine(String.Format("{1} contracts for {0} - won't be hosted", endpoint.Name, serviceTypes.Length));
                    Console.ForegroundColor = ConsoleColor.Gray;
                    continue;
                }
                foreach (var serviceType in serviceTypes)
                {
                    ServiceHost host;
                    if (!_hosts.TryGetValue(serviceType, out host))
                    {
                        host = new ServiceHost(serviceType);
                        _hosts.Add(serviceType, host);
                        host.UnknownMessageReceived += HostOnUnknownMessageReceived;
                        host.Faulted += HostOnFaulted;
                    }
                    var bindingType = Type.GetType(endpoint.Binding);
                    if (bindingType == null)
                    {
                        Trace.WriteLine("Unable to get type for " + endpoint.Binding);
                        continue;
                    }
                    var binding = Activator.CreateInstance(bindingType) as Binding;
                    if (binding == null)
                    {
                        Trace.WriteLine("Unable to create instance of " + bindingType.Name);
                        continue;
                    }
                    var address = endpoint.Address.Replace("{host}", "localhost").Replace("{Deployment}", String.Empty);
                    host.AddServiceEndpoint(contractType, binding, address);
                    AddMex(host, address);
                    Trace.WriteLine(String.Format("{3} : {0}\n  {2}", contractType.Name, bindingType.Name, address, serviceType.Name, serviceType.Assembly.GetName().Name));
                    //Trace.WriteLine(String.Format("{3}\t{4}\t{0}\t{1}\t{2}", contractType.Name, bindingType.Name, address, serviceType.Name, serviceType.Assembly.GetName().Name));
                }
            }
        }

        void AddMex(ServiceHost host, string address)
        {
            var behavior = host.Description.Behaviors.Find<ServiceMetadataBehavior>();
            if (behavior == null)
            {
                behavior = new ServiceMetadataBehavior
                {
                    MetadataExporter = { PolicyVersion = PolicyVersion.Policy15 }
                };
                host.Description.Behaviors.Add(behavior);
            }
            host.AddServiceEndpoint(ServiceMetadataBehavior.MexContractName,
                CreateMexBinding(address), address + "/mex");
        }

        Binding CreateMexBinding(string address)
        {
            var uriBuilder = new UriBuilder(address);
            switch (uriBuilder.Scheme)
            {
                case "net.tcp":
                    return MetadataExchangeBindings.CreateMexTcpBinding();
                case "net.pipe":
                    return MetadataExchangeBindings.CreateMexNamedPipeBinding();
                case "https":
                    return MetadataExchangeBindings.CreateMexHttpsBinding();
                default:
                    return MetadataExchangeBindings.CreateMexHttpBinding();
            }
        }

        string GetTraceHostInfo(object hostObject)
        {
            var host = (ServiceHost)hostObject;
            return host.Description.ServiceType.Name;
        }

        void HostOnFaulted(object sender, EventArgs e)
        {
            Trace.WriteLine(GetTraceHostInfo(sender) + " faulted");
        }

        void HostOnUnknownMessageReceived(object sender, UnknownMessageReceivedEventArgs e)
        {
            Trace.WriteLine(GetTraceHostInfo(sender) + " received unknown message:\n" + e.Message);
        }

        #endregion

        #region public meths

        public void Start()
        {
            foreach (var host in _hosts.Values)
            {
                try
                {
                    host.Open();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Trace.WriteLine("Hosting " + host.Description.Name);
                    Console.ForegroundColor = ConsoleColor.Gray;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Trace.WriteLine("Unable to start " + host.Description.Name + " " + ex.Message);
                    Console.ForegroundColor = ConsoleColor.Gray;
                }
            }
        }

        public void Stop()
        {
            foreach (var host in _hosts.Values)
            {
                Trace.Write("Stopping " + host.Description.Name + "...");
                try
                {
                    host.Close();
                    Trace.WriteLine(" done.");
                }
                catch
                {
                    try
                    {
                        Trace.Write(" error. Aborting...");
                        host.Abort();
                        Trace.WriteLine(" done.");
                    }
                    catch
                    {
                        Trace.WriteLine(" error.");
                    }
                }
            }
        }

        #endregion
    }
}
