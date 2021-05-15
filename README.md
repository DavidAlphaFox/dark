# Dark

This is the main repo for [Dark](https://darklang.com), a combined language, editor, and infrastructure to make it easy to build backends.

This repo is intended to help Dark users solve their needs by fixing bugs, expanding features, or otherwise contributing. Dark is
[source available, not open source](https://github.com/darklang/dark/blob/main/LICENSE.md).

## Contributing

We are committed to make Dark easy to contribute to.
Our [contributor
docs](https://darklang.github.io/docs/contributing/getting-started) will help
guide you through your first PR, find good projects to contribute to, and learn
about the code base.

## Getting Started

### Install dependencies

We develop Dark within a docker container, so there is not a lot of setup.
However, we do need to setup the host system in a few ways to support file
watching, DNS, and of course Docker. This section guides you through that, for
each OS.

#### OSX

To build and run the server you must have the following installed (and running):

- Homebrew for Mac (https://brew.sh/)
- Docker for Mac (https://docs.docker.com/docker-for-mac/install/)
- The latest version of bash `brew install bash`
- fswatch `brew install fswatch`
- PIP `brew install python`
- live reload `pip3 install livereload`

##### Docker

The app and its dependencies are all held within the container. While code is edited on your machine, the application is compiled and run inside of the container.

Ensure that docker:

- set to use 4 CPUs, 4.0 GiB of Memory, and 4.0 GiB of Swap (under the Advanced preferences tab).

Ignore the other tabs (for example you don't need to enable Kubernetes).

##### Dnsmasq

A local DNS server is needed to access the application via a `.localhost` TLD. The following is a quick start, adapted from [this guide](https://passingcuriosity.com/2013/dnsmasq-dev-osx/).

Install dnsmasq:

```
brew install dnsmasq / apt install dnsmasq
```

Follow brew's post-install instructions:

```
brew info dnsmasq
```

(probably `sudo brew services start dnsmasq`)

Add the following to `(brew --prefix)/etc/dnsmasq.conf`

```
address=/localhost/127.0.0.1
```

Restart dnsmasq:

```
sudo brew services restart dnsmasq
```

Configure OSX to use dnsmasq (not needed on linux):

```
sudo mkdir -p /etc/resolver
sudo tee /etc/resolver/localhost >/dev/null <<EOF
nameserver 127.0.0.1
EOF
```

Test it:

```
# Make sure you haven't broken your DNS.
ping -c 1 www.google.com
# Check that .localhost names work
dig testing.builtwithdark.localhost@127.0.0.1
```

#### Windows

On Windows, you can run Dark in WSL2 (Windows Subsystem for Linux):

- You must be on at least Windows 10 Version 2004, and you must run WSL 2 (docker does not work in WSL 1)
- Follow the [WSL 2 installation instructions](https://docs.microsoft.com/en-us/windows/wsl/install-win10#update-to-wsl-2)
- Follow the [Docker for WSL 2 installation instructions](https://docs.docker.com/docker-for-windows/wsl/)
- After installing a Linux distro, run the depedencies below in your Linux distro
- This section of the guide is incomplete. We would welcome further notes to make this foolproof.

#### On Linux

##### Install dependencies

To build and run the server you must have the following installed (and running):

- fswatch: `apt install fswatch`
- PIP: `apt install python3-pip`
- live reload: `pip3 install livereload`

### Dnsmasq

A local DNS server is needed to access the application via a `.localhost` TLD. The following is a quick start, adapted from [this guide](https://passingcuriosity.com/2013/dnsmasq-dev-osx/).

Install dnsmasq:

```
apt install dnsmasq
```

Add the following to `/etc/dnsmasq.conf`

```
address=/localhost/127.0.0.1
```

Restart dnsmasq:

```
sudo /etc/init.d/dnsmasq restart
```

Test it:

```
# Make sure you haven't broken your DNS.
ping -c 1 www.google.com
# Check that .localhost names work
dig testing.builtwithdark.localhost@127.0.0.1
```

### Building and running for the first time

Now that the pre-requisites are installed, we should be able to build the
development container in Docker, which has the exact right versions of all the
tools we use.

- Run `scripts/builder --compile --watch --test`
- Wait until the terminal says "Initial compile succeeded" - this means the
  build server is ready. The `builder` script will sit open, waiting for file
  changes in order to recompile
- If you see "initial compile failed", it may be a memory issue. Ensure you
  have docker configured to provide 4GB or more of memory, then rerun the builder
  script. (Sometimes just rerunning will work, too).
- Open your browser to http://darklang.localhost:8000/a/dark/, username "dark",
  password "what"
- Edit code normally - on each save to your filesystem, the app will be rebuilt
  and the browser will reload as necessary

## Read the contributor docs

If you've gotten this far, you're now ready to [contribute your first PR](https://darklang.github.io/docs/contributing/getting-started#first-contribution).

## Testing

Unit tests run when you specify `--test` to `scripts/builder`. You can run them as a once off using:

- `scripts/runtests` # client
- `scripts/run-backend-tests`
- `scripts/run-rust-tests containers/stroller`
- `scripts/run-rust-tests containers/queue-scheduler`

Integration tests:

- `scripts/run-in-docker ./integration-tests/run.sh`

You can also run integration tests on your (host) machine, which gives you some debugging ability, and typically runs faster:

- `./integration-tests/run.sh`

There are good debugging options for integration testing. See integration-tests/README.

## Running unix commands in the container

- `scripts/run-in-docker bash`

## Accessing the local db

- `scripts/run-in-docker psql -d devdb`

## Config files

Config files are in config/. Simple rule: anything that runs inside the
container must use a DARK_CONFIG value set in config/, and cannot use
any other env var.

## Debugging the client

You can enable the FluidDebugger by mousing over the Gear in the
left-sidebar. There is also "Enable debugger" which enables a legacy
debugger that nobody uses and doesn't work well.

If you're using Chrome, enable Custom Formatters to see ReScript values in
Chrome Dev Tools instead of their JS representation. From within Chrome
Dev Tools, click "⠇", "Settings", "Preferences", "Enable Custom
Formatters".

## Debugging dotnet

### REPL (fsi)

You can get a REPL with all of the Dark libraries loaded by running:

- `scripts/dotnet-fsi`

### Segfaults and crashes

When dotnet crashes, you can debug it by running:

- `lldb -- [your command]

In LLDB, you can use dotnet's SOS plugin to read the stack, values, etc. See
https://docs.microsoft.com/en-us/dotnet/framework/tools/sos-dll-sos-debugging-extension
for instructions. The plugin is automatically loaded in lldb in the dev
container.

## Production Services

The app is split into `backend/` and `client/`. Part of the backend is used in
the client (`jsanalysis`), and one directory is shared (`libshared`). These are
compiled to create libraries and binaries.

These are put into containers, whose definitions are in `containers/`. We also
have some containers which are defined entirely in their directory (typically,
these have a self-contained codebase).

The containers are used in `services/`. A _service_ is typically a number of
yaml files defining a kubernetes _deployment_, made up of one or more
containers, which use binaries from the backend.

Some services do not use Dark's containers (eg, when we deploy 3rdparty code,
such as "let's encrypt"). Some just have a single container (eg queue-scheduler
and postgres-honeytail).

## Other important docs

- [Contributor docs](https://darklang.github.io/docs/contributing/getting-started)
- [Other ways to run the dev container](docs/builder-options.md)
- [Setting up your editor](docs/editor-setup.md)
- [Dark unit tests](fsharp-backend/tests/testfiles/README.md)

### Less important docs

- [Running the client against production (ngrok)](docs/running-against-production.md)
- [Oplist serialization](docs/oplist-serialization.md)
- [Intricacies of Bucklescript-tea](docs/bs-tea.md)
- [Writing Stdlib docstrings](docs/writing-docstrings.md)
- [Editing other BS libraries](docs/modifying-libraries.md)
- [Add an account for yourself](docs/add-account.md)
