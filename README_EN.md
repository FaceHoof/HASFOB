# HASFOB (Home Assistant Service for Old Browsers)

## Description

HASFOB is a service that retrieves data from Home Assistant and publishes it as a simple HTML page and a JSON API.

The project is designed for viewing Home Assistant data in legacy web browsers that do not support the modern Home Assistant web interface.

The service periodically addresses Home Assistant, receives data and saves it only from the sensors and switches specified in the configuration, and provides:

* a simple web page;
* a JSON API;
* real-time data updates without page reloads (Server-Sent Events);
* the ability to control configured switches.

---

# Initial Configuration

Before starting the service, edit the configuration.xml file and specify the required settings.

## Home Assistant Connection

| Parameter                | Description                                                        |
| ------------------------ | ------------------------------------------------------------------ |
|  homeAssistantServer     | Home Assistant server address (for example, http://192.168.1.10)   |
|  homeAssistantPort       | Home Assistant port (typically  8123 )                             |
|  homeAssistantTokenFile  | Path to the file containing the Long-Lived Access Token            |
|  servicePort             | Port on which HASFOB will listen                                   |

---

## Sensors

At least one sensor must be configured.

Example:

<sensors>
    <sensor>
        <entityId>sensor.outdoor_temperature</entityId>
        <friendlyName>Outdoor Temperature</friendlyName>
        <decimalPlaces>1</decimalPlaces>
    </sensor>

    <sensor>
        <entityId>sensor.humidity</entityId>
    </sensor>
</sensors>

Sensor parameters:

| Parameter       | Description                        |
| --------------- | ---------------------------------- |
|  entityId       | Sensor Entity ID in Home Assistant |
|  friendlyName   | Display name                       |
|  decimalPlaces  | Number of decimal places           |

---

## Switches (Optional)

If you want to control switches through the web interface, add them to the following section:

<switches>
    <switch>
        <entityId>switch.light_room</entityId>
        <friendlyName>Lighting</friendlyName>
    </switch>
</switches>

---

## Update Interval

<updateIntervalSeconds>30</updateIntervalSeconds>

The minimum allowed value is 10 seconds.

---

## Log Retention

<logRetentionDays>30</logRetentionDays>

Specifies how many days log files are retained.

---

## Display Area and Device Names

<showAreaNames>Y</showAreaNames>
<showDeviceNames>Y</showDeviceNames>

Possible values:

* Y — display;
* N — hide.

---

# Token File

The file specified in homeAssistantTokenFile must contain only the Home Assistant Long-Lived Access Token, without any additional characters, comments, or extra text.

---

# Running the Service

After startup, the service begins contacting Home Assistant for the data at regular intervals and starts its HTTP server.

---

# API

The service provides the following HTTP endpoints:

| Method | Endpoint             | Description                              |
| ------ | -------------------- | ---------------------------------------- |
| GET    | /                    | HTML page                                |
| GET    | /api/all             | All data                                 |
| GET    | /api/sensors         | Sensors only                             |
| GET    | /api/switches        | Switches only                            |
| POST   | /api/switch/toggle   | Toggle a switch                          |
| GET    | /api/events          | Server-Sent Events for live data updates |

---

# Customizing the Web Page

The service directory contains a file named WebPage.html. This file defines the appearance of the web page.

If necessary, you can modify its contents to customize the interface.

Be sure to make a backup copy of this file before making any changes!

---

# Running on Windows

Under Windows, the application can be run either as a regular program or installed as a Windows Service.

To install the service, open a Command Prompt with Administrator privileges and run:

HASFOB.exe --install

or

HASFOB.exe -i

After installation, a Windows Service named HASFOB will be created and configured to start automatically when the system boots.

To uninstall the service, run:

HASFOB.exe --uninstall

or

HASFOB.exe -u

To run the application without installing it as a service, simply start HASFOB.exe without any command-line arguments.

---

# Running on Linux

Navigate to the service directory and execute the following commands:

chmod +x HasfobService
./HasfobService

To display the command-line help, start the application with the -h parameter.

## System Requirements

Software Requirements:
Runtime: .NET 8 Runtime (or .NET 8 SDK for building)  
OS: Windows 10 / Windows Server 2016+ or Linux (Debian, Ubuntu, etc. with systemd)
External Integration: Home Assistant (REST API, Long-Lived Access Token)
Hardware Requirements:
CPU: 1 GHz or faster (x64 / ARM64)
RAM: 1 GB (Linux) / 2 GB (Windows)
Disk Space: ~150 MB (for .NET Runtime and application)
