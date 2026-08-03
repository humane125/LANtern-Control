LANtern Control AppImage - Linux test build v0.1.0
=================================================

The AppImage contains the .NET runtime, LANtern Control, its Avalonia graphics
libraries, and libpcap. It does not need to be extracted or installed.

Run on Linux Mint
-----------------
1. Copy LANtern-Control-v0.1.0-x86_64.AppImage into your Home folder.
2. Right-click the AppImage, open Properties > Permissions, and enable
   "Allow executing file as program". You only do this once.
3. Double-click the AppImage.

4. On the first launch (and after an update), Linux shows its normal graphical
   administrator-password prompt. LANtern installs a protected copy under
   /opt/lantern-control and grants only CAP_NET_RAW + CAP_NET_ADMIN. The GUI
   continues as your normal user; later launches do not ask again.

5. If the system does not have FUSE support, use the extraction fallback:

     APPIMAGE_EXTRACT_AND_RUN=1 ./LANtern-Control-v0.1.0-x86_64.AppImage

6. Select the adapter whose gateway is your router, click Start control, then
   click Refresh devices.

Always click Stop & restore before closing LANtern, changing networks,
disconnecting the adapter, suspending, or rebooting.

This build controls IPv4 traffic. Test only on a network you own or administer.
