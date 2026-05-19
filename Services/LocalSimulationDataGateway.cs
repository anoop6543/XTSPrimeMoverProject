using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace XTSPrimeMoverProject.Services
{
	/// <summary>
	/// In-process data gateway implementing the data service contract directly over SimulationDataLogger.
	/// </summary>
	public sealed class LocalSimulationDataGateway : IDataGatewayService
	{
		private readonly SimulationDataLogger _dataLogger;
		private readonly ErrorHandlingService _errorHandler = ErrorHandlingService.Instance;

		public LocalSimulationDataGateway(SimulationDataLogger dataLogger)
		{
			_dataLogger = dataLogger ?? throw new ArgumentNullException(nameof(dataLogger));
		}

		public string DatabasePath => _dataLogger.DatabasePath;

		public Task<IReadOnlyList<PartHistoryEventRecord>> GetPartHistoryAsync(string trackingNumber, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(_errorHandler.ExecuteWithRetry(
				() => (IReadOnlyList<PartHistoryEventRecord>)_dataLogger.GetPartHistory(trackingNumber),
				"LocalDataGateway.GetPartHistory",
				ErrorCategory.Gateway,
				fallback: Array.Empty<PartHistoryEventRecord>())!);
		}

		public Task<PartSummaryRecord?> GetPartSummaryAsync(string trackingNumber, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(_errorHandler.ExecuteWithRetry(
				() => _dataLogger.GetPartSummary(trackingNumber),
				"LocalDataGateway.GetPartSummary",
				ErrorCategory.Gateway,
				fallback: null));
		}

		public Task<IReadOnlyList<string>> GetExportableTablesAsync(CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(_errorHandler.ExecuteWithRetry(
				() => (IReadOnlyList<string>)_dataLogger.GetExportableTables(),
				"LocalDataGateway.GetExportableTables",
				ErrorCategory.Gateway,
				fallback: Array.Empty<string>())!);
		}

		public Task<IReadOnlyList<string>> GetAllTablesAsync(CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(_errorHandler.ExecuteWithRetry(
				() => (IReadOnlyList<string>)_dataLogger.GetAllTables(),
				"LocalDataGateway.GetAllTables",
				ErrorCategory.Gateway,
				fallback: Array.Empty<string>())!);
		}

		public Task<IReadOnlyList<string>> GetTableColumnsAsync(string tableName, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(_errorHandler.ExecuteWithRetry(
				() => (IReadOnlyList<string>)_dataLogger.GetTableColumns(tableName),
				"LocalDataGateway.GetTableColumns",
				ErrorCategory.Gateway,
				fallback: Array.Empty<string>())!);
		}

		public Task<int> GetTableRowCountAsync(string tableName, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(_errorHandler.ExecuteWithRetry(
				() => _dataLogger.GetTableRowCount(tableName),
				"LocalDataGateway.GetTableRowCount",
				ErrorCategory.Gateway,
				fallback: 0));
		}

		public Task<IReadOnlyList<Dictionary<string, string>>> GetTableRowsAsync(string tableName, int maxRows = 500, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(_errorHandler.ExecuteWithRetry(
				() => (IReadOnlyList<Dictionary<string, string>>)_dataLogger.GetTableRows(tableName, maxRows),
				"LocalDataGateway.GetTableRows",
				ErrorCategory.Gateway,
				fallback: Array.Empty<Dictionary<string, string>>())!);
		}

		public Task<string> ExportTableToCsvAsync(string tableName, string? exportDirectory = null, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(_errorHandler.ExecuteWithRetry(
				() => _dataLogger.ExportTableToCsv(tableName, exportDirectory),
				"LocalDataGateway.ExportTableToCsv",
				ErrorCategory.Gateway,
				fallback: string.Empty)!);
		}

		public string GetDefaultExportDirectory()
		{
			return _dataLogger.DatabasePath is string
				? System.IO.Path.Combine(AppContext.BaseDirectory, "Exports")
				: System.IO.Path.Combine(AppContext.BaseDirectory, "Exports");
		}
	}
}
