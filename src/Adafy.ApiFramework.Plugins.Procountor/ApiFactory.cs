using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Adafy.ApiFramework.Plugins.Procountor
{
    public static class ApiFactory
    {
        public static Task<IEnumerable<Type>> Create(ProcountorOptions configuration)
        {
            return Task.FromResult<IEnumerable<Type>>(new List<Type>() { typeof(ProcountorProxy) });
        }
    }
}
