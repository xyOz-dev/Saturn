using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Saturn.Core.Abstractions.ProgressReporters
{
    public class CompositeProgressReporter : IProgressReporter
    {
        private readonly List<IProgressReporter> _reporters = new();
        
        public void AddReporter(IProgressReporter reporter)
        {
            if (reporter == null)
                throw new ArgumentNullException(nameof(reporter));
                
            _reporters.Add(reporter);
        }
        
        public void RemoveReporter(IProgressReporter reporter)
        {
            _reporters.Remove(reporter);
        }
        
        public async Task ReportStatusAsync(StatusUpdate status)
        {
            var tasks = _reporters.Select(r => r.ReportStatusAsync(status));
            await Task.WhenAll(tasks);
        }
        
        public async Task ReportProgressAsync(ProgressUpdate progress)
        {
            var tasks = _reporters.Select(r => r.ReportProgressAsync(progress));
            await Task.WhenAll(tasks);
        }
        
        public async Task ReportToolExecutionAsync(ToolExecutionUpdate toolExecution)
        {
            var tasks = _reporters.Select(r => r.ReportToolExecutionAsync(toolExecution));
            await Task.WhenAll(tasks);
        }
        
        public async Task ReportErrorAsync(ErrorUpdate error)
        {
            var tasks = _reporters.Select(r => r.ReportErrorAsync(error));
            await Task.WhenAll(tasks);
        }
        
        public async Task ReportCompletionAsync(CompletionUpdate completion)
        {
            var tasks = _reporters.Select(r => r.ReportCompletionAsync(completion));
            await Task.WhenAll(tasks);
        }
    }
}