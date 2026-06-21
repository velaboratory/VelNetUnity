# Self-hosting the Server

VelNet's server can be self-hosted with modest resources. The limiting factor will almost always be network bandwidth.

=== "Option 1: Pull from Docker Hub"

    Make sure you have [Docker](https://www.docker.com/) installed, then pull the Docker image using:

    ```sh
    docker run -p 5000:5000 -p 5000:5000/udp velaboratory/velnet
    ```

    or

    ```sh
    docker run -p 5050:5000 -p 5050:5000/udp --name velnet velaboratory/velnet
    ```

    to run on a different port and change the name of the container.

=== "Option 2: Use docker-compose"

    1. Clone the repo
        - `git clone https://github.com/velaboratory/VelNetServerRust.git`

    - Make sure you have [Docker](https://www.docker.com/) and [docker-compose](https://docs.docker.com/compose/) installed.

    - The docker compose file runs both the control panel and the server.
        - To start:
            ```sh
            docker compose up -d
            ```
        - To stop:

            ```sh
            docker compose stop
            ```


    This builds the images from the local data in the folder, and doesn't pull anything from Docker Hub.

=== "Option 3: Run Rust natively"

    1. Clone the repo
        - `git clone https://github.com/velaboratory/VelNetServerRust.git`
    - Edit `config.json` to an open port on your firewall
    - Modify the `user` field in `control-panel/config.json` to be your username.
    - Install rust through using [rustup](https://rustup.rs/)
    - Install: `sudo ./install.sh`
    - Run server: `sudo systemctl start velnet`
    - Run control panel: `sudo systemctl start velnet-control-panel`
    - Install tuptime: `cargo install tuptime`
    - Install onefetch: `cargo install onefetch`
