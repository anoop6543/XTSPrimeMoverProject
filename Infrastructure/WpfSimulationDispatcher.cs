using System;
using System.Windows.Threading;
using XTSPrimeMoverProject.Services;

namespace XTSPrimeMoverProject.Infrastructure
{
    /// <summary>Marshals engine notifications onto the WPF UI thread.</summary>
    public sealed class WpfSimulationDispatcher : ISimulationDispatcher
    {
        private readonly Dispatcher _dispatcher;

        public WpfSimulationDispatcher(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public bool IsShuttingDown => _dispatcher.HasShutdownStarted;
        public bool CheckAccess() => _dispatcher.CheckAccess();
        public void Invoke(Action action) => _dispatcher.Invoke(action);
        public void BeginInvoke(Action action) => _dispatcher.BeginInvoke(action);
    }
}
