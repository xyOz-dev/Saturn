using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Saturn.Core.Abstractions.ProgressReporters
{
    public class CompositeProgressReporter : IProgressReporter
    {
        private readonly List<IProgressReporter> _reporters = new();
        private readonly object _lock = new();
        
        public void AddReporter(IProgressReporter reporter)
        {
            if (reporter == null)
                throw new ArgumentNullException(nameof(reporter));
                
            lock (_lock)
            {
                _reporters.Add(reporter);
            }
        }
        
        public void RemoveReporter(IProgressReporter reporter)
        {
            lock (_lock)
            {
                _reporters.Remove(reporter);
            }
        }
        
        public async Task ReportStatusAsync(StatusUpdate status)
        {
            IProgressReporter[] snapshot;
            lock (_lock)
            {
                snapshot = _reporters.ToArray();
            }
            
            if (snapshot.Length == 0)
                return;
                
            var tasks = snapshot.Select(r => r.ReportStatusAsync(status));
            await Task.WhenAll(tasks);
        }
        
        public async Task ReportProgressAsync(ProgressUpdate progress)
        {
            IProgressReporter[] snapshot;
            lock (_lock)
            {
                snapshot = _reporters.ToArray();
            }
            
            if (snapshot.Length == 0)
                return;
                
            var tasks = snapshot.Select(r => r.ReportProgressAsync(progress));
            await Task.WhenAll(tasks);
        }
        
        public async Task ReportToolExecutionAsync(ToolExecutionUpdate toolExecution)
        {
            IProgressReporter[] snapshot;
            lock (_lock)
            {
                snapshot = _reporters.ToArray();
            }
            
            if (snapshot.Length == 0)
                return;
                
            var tasks = snapshot.Select(r => r.ReportToolExecutionAsync(toolExecution));
            await Task.WhenAll(tasks);
        }
        
        public async Task ReportErrorAsync(ErrorUpdate error)
        {
            IProgressReporter[] snapshot;
            lock (_lock)
            {
                snapshot = _reporters.ToArray();
            }
            
            if (snapshot.Length == 0)
                return;
                
            var tasks = snapshot.Select(r => r.ReportErrorAsync(error));
            await Task.WhenAll(tasks);
        }
        
        public async Task ReportCompletionAsync(CompletionUpdate completion)
        {
            IProgressReporter[] snapshot;
            lock (_lock)
            {
                snapshot = _reporters.ToArray();
            }
            
            if (snapshot.Length == 0)
                return;
                
            var tasks = snapshot.Select(r => r.ReportCompletionAsync(completion));
            await Task.WhenAll(tasks);
        }
    }
}