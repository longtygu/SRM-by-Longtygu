using LibreHardwareMonitor.Hardware;

namespace SRM_by_Longtygu.Tools.HardwareInfo
{
    // Boilerplate bắt buộc của LibreHardwareMonitorLib: duyệt qua toàn bộ Hardware/SubHardware
    // để gọi Update() và làm mới giá trị các cảm biến (Sensors).
    internal class HardwareUpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer)
        {
            computer.Traverse(this);
        }

        public void VisitHardware(IHardware hardware)
        {
            try
            {
                hardware.Update();
            }
            catch
            {
                // Bỏ qua phần cứng bị lỗi khi đọc (thường do thiếu quyền/driver),
                // không để ảnh hưởng tới việc cập nhật các phần cứng khác trong cùng chu kỳ.
            }

            foreach (var subHardware in hardware.SubHardware)
            {
                subHardware.Accept(this);
            }
        }

        public void VisitSensor(ISensor sensor) { }

        public void VisitParameter(IParameter parameter) { }
    }
}
