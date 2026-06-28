using Microsoft.Extensions.DependencyInjection;
using Warrant.Application;

namespace Warrant.Infrastructure;

public static class SignalSourceRegistration
{
    public static IServiceCollection AddAdaptiveSignalSources(this IServiceCollection s, WarrantCapabilities caps)
    {
        if (caps.Purview) 
        {
            s.AddSingleton<IExternalSignalSource, PurviewSignalSource>();
        }
        else 
        {
            s.AddSingleton<IExternalSignalSource, FloorPermissionSignalSource>();
        }
        return s;
    }
}