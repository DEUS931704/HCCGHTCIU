// Services/IIpLookupService.cs - 定義 IP 查詢服務介面

using HCCGHTCIU.Models; // 引用模型命名空間
using System.Threading.Tasks; // 引用任務相關命名空間

namespace HCCGHTCIU.Services
{
    // IP 查詢服務的介面定義
    public interface IIpLookupService
    {
        // 異步方法：根據 IP 地址查詢 IP 信息
        Task<IpLookupResult> LookupIpAsync(string ipAddress);
    }
}