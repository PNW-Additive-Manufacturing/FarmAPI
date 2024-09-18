# FarmAPI
A comprehensive HTTP API for managing multiple 3D printers, including real-time status tracking, printer control, and automatic slicing capabilities.

Supported Machines:
- [x] Bambu Lab: X1C, P1P, P1S, X1E, A1, and A1 Mini.

Supported Slicers:
- [x] Bambu Studio (Preferred Slicer)
- [ ] Prusa Slicer
- [ ] Orca Slicer 

## Utilizing the FarmAPI

```
GET /printers
```

Responds with the statuses of all configured machines.

```json
{
    "Cinder Block": {
        "filaments": [
            {
                // Location can be AMS or External.
                "location": "AMS",
                "slot": 0,
                // A hex color-code with FF at the end (May be changed in the future)
                "color": "FFFFFFFF",
                "material": "PLA"
            },
            {
                "location": "AMS",
                "slot": 1,
                "color": "46A8F9FF",
                "material": "PLA"
            },
            {
                "location": "AMS",
                "slot": 2,
                "color": "7C4B00FF",
                "material": "PLA"
            },
            {
                "location": "AMS",
                "slot": 3,
                "color": "000000FF",
                "material": "PLA"
            }
        ],
        "size": {
            "width": 256,
            "length": 256,
            "height": 256
        },
        "isHealthy": true,
        "lastUpdated": "2024-09-18T21:56:42.3926174+00:00",
        "status": "Printed",
        "failReason": null,
        "identifier": "Cinder Block",
        "brand": "BBL",
        "model": "X1C",
        "progress": 100,
        // Time remaining in seconds.
        "timeRemaining": 0,
        "filename": "Logo (6).stl"
    }
}
```

<hr/>

```
POST /printers/print

Query Parameters:
string fileName
string material
string colorHex
float layerHeight
bool? useSupports
string? supportStyle
int? wallLoops
int quantity = 1
```

Queries an available printer containing the specified filament material and color, slices, and begins printing the given STL automatically.

```json
{
    "success": false,
    // The error message.
    "message": "No available machines with matching filament!",
    // If successful, the identifier (serial-number or nickname) of the printer sent to.
    "identifier": "Cinder Block"
}
```

<hr/>

```
POST /slicing/slice/info

Query Parameters:
string fileName
string manufacturer
string model
string material
string colorHex
float layerHeight
bool? useSupports
string? supportStyle
int? wallLoops
int quantity = 1
```

Responds with the metadata of a sliced STL.

```json
{
    "success": true,
    "durationAsSeconds": 1331
}
```

<hr/>

```
POST /printers/{identity}/markAsCleared
```

Marks the specified machine as cleared, and sets the status from `Error` or `Printed` back to `Idle` (Nothing on plate).

> This action is an optional implementation on a per-machine basis.

## Configuring the Application

FarmAPI uses JSON or Environment Variables to configure the machines, credentials, and other options. 

### Using JSON

The location of the JSON file is determined with the `FARMAPI_CONFIG_PATH` environment variable or the `config.json` file in the working directory of the process.

```json
{
    // Credentials are used to communicate with Bambu Lab printers through Bambu Cloud instead of local to 
    // ensure the useability of the Bambu Lab Handy app.
    "bambuCloudCredentials": {
        "email": "Bambu Cloud Email",
        "password": "Bambu Cloud Password"
    },
    // The full-path to the root folder of Bambu Studio.
    "bambuStudioPath": "/BambuStudio",
    "machines": [
        {
            "kind": "BambuLab",
            "serialNumber": "15 Character-Serial Number",
            "accessCode": "8 Character-Code",
            "ipAddress": "xxx.xxx.xxx.xxx",
            // An optional field declaring a nickname, default identifier will be the serialNumber.
            "nickname": "Kachow"
        }
    ]
}
```

## Using the Docker Container and Environment Variables

Use the provided `Dockerfile` in the root of the project to generate a Docker image using `docker image build -t farm-api .`. This process will elapse 20-30 minutes depending on if you have already performed the `bambu-setup` step.

Running the image requires you to specify either `FARMAPI_CONFIG_PATH` or supplement using environment variables:
```
BAMBU_CLOUD_EMAIL
BAMBU_CLOUD_PASSWORD
BAMBU_STUDIO_MACHINES_PATH
BAMBU_STUDIO_PATH (Optional as included in Dockerfile)
BAMBU_STUDIO_EXECUTABLE_PATH (Optional)
BAMBU_STUDIO_RESOURCES_PATH (Optional)
```

You should have noticed the `BAMBU_STUDIO_MACHINES_PATH` variable, since we cannot specify the configurations for each machine using  
environment variables, a path to a JSON file containing ONLY an array of machine configurations are required.