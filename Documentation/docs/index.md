# VelNet

VelNet is an easy-to-use networking library for Unity. VelNet is architected similar to [Photon PUN](https://doc.photonengine.com/pun/current/getting-started/pun-intro), with a single server that acts as a relay for information sent by all clients.

VelNet consists of two main parts, [VelNet Server](/server) and [VelNetUnity](/client).

<!-- ### [VelNet Server](/server)

[GitHub Link :simple-github:](https://github.com/velaboratory/VelNetServerRust)

### VelNet Unity

[GitHub Link :simple-github:](https://github.com/velaboratory/VelNetUnity) -->

## Installation

1. Set up the [server](/server), or use the default server at `velnet-example.ugavel.com`

- Install the UPM package in Unity:

=== "**Option 1:** Add the VEL package registry"

    ![Scoped registry example](/assets/screenshots/scoped_registry.png){ align=right }

    Using the scoped registry allows you to easily install a specific version of the package by using the Version History tab.

    - In Unity, go to `Edit->Project Settings...->Package Manager`
    - Under "Scoped Registries" click the + icon
    - Add the following details, then click Apply
        - Name: `VEL`
        - URL: `https://npm.ugavel.com`
        - Scope(s): `edu.uga.engr.vel`
    - Install the package:
        - In the package manager, select `My Registries` from the dropdown
        - Install the `VelNet` package.

=== "**Option 2:** Add the package by git url"

    1. Open the Package Manager in Unity with `Window->Package Manager`
    - Add the local package:
        - `+`->`Add package from git URL...`
        - Select the path to `https://github.com/velaboratory/VelNetUnity#upm`

    To update the package, click the `Update` button in the Package Manager, or delete the `packages-lock.json` file.

=== "**Option 3:** Add the package locally"

    1. Clone the repository on your computer:
        `git clone git@github.com:velaboratory/VelNetUnity.git`
    - Open the Package Manager in Unity with `Window->Package Manager`
    - Add the local package:
        - `+`->`Add package from disk...`
        - Select the path to `VelNetUnity/TestVelGameServer/Packages/VelNetUnity/package.json`

    To update the package, use `git pull` in the VelNetUnity folder.

Then check out the [samples](client/samples), or follow the [quick start](/client/quick-start).