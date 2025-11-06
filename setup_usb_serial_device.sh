#!/bin/bash
# USB serial gadget setup for Raspberry Pi 5
# Requires: otg_mode=1 in /boot/firmware/config.txt
# Run as root

set -e  # Exit on error

G=/sys/kernel/config/usb_gadget/g1

# Clean up existing gadget if present
if [ -d "$G" ]; then
    echo "Cleaning up existing gadget..."
    [ -f "$G/UDC" ] && echo "" > "$G/UDC" 2>/dev/null || true
    
    # Remove symlinks first
    find "$G/configs" -type l -delete 2>/dev/null || true
    
    # Remove directories in reverse order
    rm -rf "$G/configs/c.1/strings" 2>/dev/null || true
    rmdir "$G/configs/c.1" 2>/dev/null || true
    rmdir "$G/configs" 2>/dev/null || true
    rmdir "$G/functions/acm.usb0" 2>/dev/null || true
    rmdir "$G/functions" 2>/dev/null || true
    rmdir "$G/strings/0x409" 2>/dev/null || true
    rmdir "$G/strings" 2>/dev/null || true
    rmdir "$G" 2>/dev/null || true
    
    sleep 1
fi

# Create gadget directory
mkdir -p $G
cd $G

# Device descriptor
echo 0x1d6b > idVendor    # Linux Foundation
echo 0x0104 > idProduct   # Multifunction Composite Gadget
echo 0x0100 > bcdDevice   # Device version 1.0.0
echo 0x0200 > bcdUSB      # USB 2.0

# Device strings
mkdir -p strings/0x409
echo "fedcba9876543210" > strings/0x409/serialnumber
echo "Nanoptix" > strings/0x409/manufacturer
echo "PayCheck 4 Serial" > strings/0x409/product

# Configuration
mkdir -p configs/c.1/strings/0x409
echo "Serial Configuration" > configs/c.1/strings/0x409/configuration
echo 250 > configs/c.1/MaxPower

# Create ACM (serial) function
mkdir -p functions/acm.usb0

# Link function to configuration
ln -s functions/acm.usb0 configs/c.1/

# Bind to UDC
UDC=$(ls /sys/class/udc | head -n 1)
echo "Binding to UDC: $UDC"
echo "$UDC" > UDC

echo "SUCCESS: USB serial gadget configured"
echo "Device should appear as /dev/ttyGS0"
