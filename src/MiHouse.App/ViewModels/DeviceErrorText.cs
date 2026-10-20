using MiHouse.Core.Cloud;
using MiHouse.Core.Lan;

namespace MiHouse.App.ViewModels;

public static class DeviceErrorText
{
    public static string Describe(Exception exception) =>
        exception switch
        {
            LanOutcomeUnknownException => "命令可能已执行，但设备未回复确认。请先在设备上确认状态，避免重复操作。",
            MiHomeException { IsDeviceError: true } cloud => $"设备拒绝了该操作（代码 {cloud.Code}）。通常表示当前模式不支持该设置，或设备暂时不可用。",
            MiHomeException cloud => $"请求失败（代码 {cloud.Code}）。",
            _ => "操作失败，请稍后重试。"
        };
}
