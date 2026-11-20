using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public interface ICafeManagementReportingService
{
    Result<CafeManagementReport> GetReport(
        CafeManagementReportRequest request);
}
