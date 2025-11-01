namespace Paycheck4.Console.Configuration
{
    public class PrinterConfig
    {
        public NetworkPrinterConfig Network { get; set; } = new();
        public UsbConfig USB { get; set; } = new();
        public ProtocolConfig Protocol { get; set; } = new();
    }

    public class NetworkPrinterConfig
    {
        public bool Enabled { get; set; }
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 9100;
    }

    public class UsbConfig
    {
        public string VendorId { get; set; } = "0xf0f";
        public string ProductId { get; set; } = "0x1001";
        public string Manufacturer { get; set; } = "Nanoptix";
        public string Product { get; set; } = "PayCheck 4";
    }

    public class ProtocolConfig
    {
        public int BufferSize { get; set; } = 4096;
        public int ResponseTimeout { get; set; } = 5000;
    }
}