using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LocalHost
{
    public class AssemblyResolver
    {
        List<string> _folders;
        List<AppDomain> _connectedDomains;

        public AssemblyResolver()
        {
            _folders = new List<string>(1);
            _connectedDomains = new List<AppDomain>(1);
        }

        public void AddFolder(string folder)
        {
            _folders.Add(folder);
        }

        public void Connect(AppDomain domain = null)
        {
            var theDomain = GetDomain(domain);
            if (!_connectedDomains.Exists(item => item == theDomain))
            {
                _connectedDomains.Add(theDomain);
                theDomain.AssemblyResolve += ResolveAssembly;
            }
        }

        Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            foreach (var folder in _folders)
            {
                var result = LoadAssembly(folder, args.Name);
                if (result != null)
                    return result;
            }
            return LoadAssembly(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), args.Name);
        }

        Assembly LoadAssembly(string path, string name)
        {
            var assemblyPath = Path.Combine(path, new AssemblyName(name).Name + ".dll");
            return File.Exists(assemblyPath) == false
                ? null
                : Assembly.LoadFrom(assemblyPath);
        }

        public void Disconnect(AppDomain domain = null)
        {
            var theDomain = GetDomain(domain);
            if (_connectedDomains.Exists(item => item == theDomain))
            {
                theDomain.AssemblyResolve -= ResolveAssembly;
                _connectedDomains.Remove(theDomain);
            }
        }

        AppDomain GetDomain(AppDomain domain)
        {
            return domain ?? AppDomain.CurrentDomain;
        }
    }
}
