# Alea GPU Tutorial

This project contains tutorial and samples of [Alea GPU](http://quantalea.com) compiler. It uses literal programming with [F# Formatting](http://tpetricek.github.io/FSharp.Formatting/) to code samples and then generates document directly from source code. The generated document can be found at [Alea GPU Tutorial](http://quantalea.com/static/app/tutorial/index.html).

## How to build

Before building the solution, please make sure you have a proper JIT license installed.

If you have one and only one GeForce GPU device attached to your host, then you can follow these steps to verify and install a free community license:

- go to solution folder and restore packages:
  - `cd MySolutionFolder`
  - `.paket\paket.bootstrapper.exe`
  - `.paket\paket.exe restore`
- in solution folder, run the following command to verify installed license for current user:
  - `packages\Alea.CUDA\tools\LicenseManager.exe list`
  - verify the output, you need a valid compilation license which support GeForce GPU
- if you don't have community license, follow these steps to install one for free:
  - go to [QuantAlea website](http://quantalea.com/accounts/login/), sign in our sign up an account
  - then go to [My Licenses page](http://quantalea.com/licenses/), to get the free community license code
  - in solution folder, run the following command to install your community license:
    - `packages\Alea.CUDA\tools\LicenseManager.exe install -l %your_license_code%`
  - verify installed license again via:
    - `packages\Alea.CUDA\tools\LicenseManager.exe list`

For more details on license, please reference:

- [License introduction](http://quantalea.com/static/app/tutorial/quick_start/licensing_and_deployment.html)
- [License Manager manual](http://quantalea.com/static/app/manual/compilation-license_manager.html)
- [Licensing page](http://quantalea.com/licensing/)

This project uses [Paket](http://fsprojects.github.io/Paket/) for package management.

To build on Windows, simply run `build.bat` from command-line under the solution folder (on Linux and OsX run `build.sh`). This script will execute the following steps:

- download latest `paket.exe` from Internet;
- run `paket.exe restore` to restore the packages listed in `paket.lock` file;
- build projects;
- runs tests;
- generate documentation (only on Windows);

If you want to skip the tests run `build.bat NoTests`. Similarly if you don't want to build the documentation run `build.bat NoDocs`.
To only build the projects run `build.bat NoTests NoDocs`.

Then you can:

- check `docs\output\index.html` for the generated document;
- execute `release\Tutorial.FS.exe <name>` to run example `<name>` written in F#.
- execute `release\Tutorial.CS.exe <name>` to run example `<name>` written in C#.
- execute `release\Tutorial.VB.exe <name>` to run example `<name>` written in VB.
- execute `release\Tutorial.FS.exe`, `release\Tutorial.CS.exe` or `release\Tutorial.VB.exe` to see more examples.
- Explore the source code with Visual Studio and run unit tests.

To build within Visual Studio, it is recommended to restore the packages before open the solution, since there is an known issue of Fody and F# project (for more details on this issue, please reference [installation manual (especially the Remarks section)](http://quantalea.com/static/app/manual/compilation-installation.html)). Please follow following steps:

- go to solution folder and restore packages:
  - `cd MySolutionFolder`
  - `.paket\paket.bootstrapper.exe`
  - `.paket\paket.exe restore`
- open solution with Visual Studio, then build solution with `Release` configuration
- set debug argument to one example, such as `examplenbodysimulation`
- run/debug the tutorial program

## How to collaborate

We use light [git-flow](https://www.atlassian.com/git/tutorials/comparing-workflows/gitflow-workflow) for collaborate. That means, the main development branch is `master`, and all feature branches should be rebased and then create merge commit onto `master` branch. This can also be done by GitHub pull request.

## Using FSharp.Core

Since [Alea GPU](http://quantalea.com) uses F#, thus F# runtime [FSharp.Core](http://www.nuget.org/packages/FSharp.Core/) is needed. In this solution, we use the [FSharp.Core](http://www.nuget.org/packages/FSharp.Core/) NuGet package for all projects, wether it is written in F#, C# or VB.

## How to upgrade packages

To upgrade packages, follow these steps:

- edit `paket.dependencies` files, edit the package or their versions; alternatively, you can use `paket install` (see [here](http://fsprojects.github.io/Paket/paket-install.html))
- execute `paket update --redirects` (see [here](http://fsprojects.github.io/Paket/paket-update.html))
- execute `package add nuget ... -i` to add that pacakge to your project, if you are adding new packages (see [here](http://fsprojects.github.io/Paket/paket-add.html))
- commit changed files, such as `paket.lock`, `paket.dependencies` and your modified project files.

If you rebase your branch onto master branch which packages have been upgraded, follow these steps:

- shutdown Visual Studio if this project is opened in Visual Studio
- do rebasing on your branch
- run `.paket\paket.exe restore` or simply run `build.bat`
- then open the Visual Studio again

The reason why we need close Visual Studio, is because Alea GPU uses Fody plugin, which is a build plugin, and Visual Studio will use it in the process, so if Fody package is upgraded, it cannot be written to `packages` folder.

