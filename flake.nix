{
  description = "Sharp-Ray, tinkering with a raytracer in C#";

  inputs = {
    nixpkgs.url = "nixpkgs/nixos-25.11";
  };

  outputs = { nixpkgs, ... }:
  let
    system = "x86_64-linux";
    pkgs = import nixpkgs { inherit system; };
  in
  {
    devShells.${system}.default = pkgs.mkShell {
      packages = with pkgs; [
        dotnet-sdk
        netcoredbg # .NET debug adapter.
        omnisharp-roslyn
      ];
    };
  };
}
