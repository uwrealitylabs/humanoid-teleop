# Humanoid Teleoperation

Unity/C# repository for Humanoid Teleoperation

# Setup

Before running the project, you need to set up the
[Humanoid Server](https://github.com/uwrealitylabs/humanoid-server) repository:

1. Copy the `.env.example` file to create your own `.env` file
2. Edit the `.env` file and replace the placeholder URL with your WebSocket
   server's address:
   ```
   URL=ws://your.ip.address:3000
   ```
   Replace `your.ip.address` with your computer's IP address (e.g.,
   `192.168.1.100`). You can find your IP address using:
   - Windows: `ipconfig | findstr /i "IPv4"` in Command Prompt
   - macOS/Linux: `ifconfig | grep 'broadcast' | awk '{print $2}'` in Terminal
3. Make sure your Quest headset and development machine are on the same network
4. The `.env` file should be in the project root directory (same level as the
   `Assets` folder)

## Unity 2022.3.24f1

Unity is the editor that we use to create 3D/XR applications. We will be using
it as our engine for the project.

1. Download the [Unity Hub](https://unity.com/download), then use it to install
   Editor version 2022.3.24f1.
2. Create a Unity account to go along with your install
3. [Clone](https://docs.github.com/en/repositories/creating-and-managing-repositories/cloning-a-repository)
   the repository.
4. Open Unity Hub, choose "Add" and select the cloned folder. This will add the
   project to your Unity Hub, where you can then launch the project.

## Oculus PC App

If you want to run the project on a Quest headset to test its functionality, you
will need to install the
[Oculus PC App](https://www.meta.com/help/quest/articles/headsets-and-accessories/oculus-rift-s/install-app-for-link/).
Note that this only supports Windows PCs. To connect your Quest headset to your
PC, you can use a Link Cable (requires USB3.0 port on your computer) or Air Link
(requires strong Wifi connection that Quest can independently connect to:
eduroam is not compatible). Once connected to your PC, launch Link Mode on your
Quest if it's not automatically started. In Unity, ensure Oculus is added to the
list of active loaders (Edit > Project Settings > Meta XR). Then, you can simply
start Play Mode and the app will be displayed to the Quest headset.
