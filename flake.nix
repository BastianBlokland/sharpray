{
  description = "Sharp-Ray, tinkering with a raytracer in C#";

  inputs = {
    nixpkgs.url = "nixpkgs/nixos-25.11";
    nixpkgs-unstable.url = "nixpkgs/nixos-unstable";
  };

  outputs = { nixpkgs, nixpkgs-unstable, ... }:
  let
    system = "x86_64-linux";
    pkgs = import nixpkgs { inherit system; };
    pkgsUnstable = import nixpkgs-unstable { inherit system; };
  in
  {
    packages.${system}.default = pkgs.buildDotnetModule {
      pname = "sharpray";
      version = "0.1.0";
      src = ./.;
      projectFile = "sharpray.csproj";
      nugetDeps = [ ];
      dotnet-sdk = pkgs.dotnet-sdk;
      dotnet-runtime = pkgs.dotnet-runtime;
    };

    devShells.${system}.default = pkgs.mkShell {
      packages = with pkgs; [
        dotnet-sdk
        pkgsUnstable.netcoredbg # .NET debug adapter.
        omnisharp-roslyn
      ];
    };
  };
}
