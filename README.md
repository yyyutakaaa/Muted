# Muted

Muted is een lightweight Windows-app die je microfoon lokaal door de officiële
[RNNoise](https://github.com/xiph/rnnoise)-library haalt. Hij is bedoeld voor
Discord, calls en gaming: constante ventilatorruis, mic-ruis en veel
toetsenbordgeluid worden onderdrukt voordat andere apps het signaal ontvangen.

De audioketen draait volledig lokaal:

```text
fysieke microfoon
  → WASAPI shared mode (48 kHz mono)
  → RNNoise (480 samples per call, 20 ms algoritmische vertraging)
  → optionele voice gate
  → driftcorrectie
  → afspeelkant van een virtuele audiokabel
  → opnamekant van die kabel in Discord / call-app / game
```

## Eerst gebruiken

Muted maakt bewust geen eigen kernel-driver aan. Windows kan een nieuwe,
selecteerbare microfoon alleen via een gesigneerde audiodriver publiceren. De
werkende eerste versie gebruikt daarom een reeds geïnstalleerde virtuele kabel,
zoals [VB-CABLE](https://vb-audio.com/Cable/).

1. Installeer een gesigneerde virtuele audiokabel en herstart Windows als de
   installer daarom vraagt.
2. Start Muted.
3. Kies je echte microfoon bij **Microfoon**.
4. Kies de afspeelkant van de kabel bij **Uitgang naar virtuele kabel**. Voor
   VB-CABLE is dit meestal `CABLE Input` of `CABLE In 16ch`.
5. Druk op de grote aan/uitknop.
6. Kies in Discord, Teams, Zoom of je game de opnamekant van de kabel als
   microfoon. Voor VB-CABLE is dit meestal `CABLE Output` of `CABLE Out 16ch`.

Muted weigert bewust naar gewone speakers of een headset te starten. Zo kan een
ontbrekende kabel bij autostart geen verborgen microfoonfeedback veroorzaken.
Zie [docs/VIRTUAL-CABLE.md](docs/VIRTUAL-CABLE.md) voor de volledige installatie,
48 kHz-instelling en probleemoplossing.

Zet ingebouwde noise suppression in de call-app bij voorkeur uit als je
onnatuurlijk pompen of vervorming hoort; twee suppressors achter elkaar geven
niet altijd een beter resultaat.

## Functies

- officiële, vastgepinde Xiph RNNoise full-model build;
- 48 kHz mono WASAPI-capture en event-driven render;
- live dry/wet-regeling, met een correct vertraagd dry-pad;
- optionele RNNoise-VAD gate voor extra stilte wanneer je niet praat;
- adaptieve één-sample driftcorrectie voor lange sessies;
- input/outputmeters, hotplug-refresh, traymodus en optionele autostart;
- instellingen in `%LOCALAPPDATA%\Muted\settings.json`;
- foutlog in `%LOCALAPPDATA%\Muted\Muted.log`;
- geen account, cloud, telemetrie of opgenomen audiobestanden.

RNNoise is noise suppression, geen acoustic echo cancellation. Geluid uit
speakers dat opnieuw de microfoon binnenkomt vraagt om AEC of een headset.
Harde toetsenbordklikken kunnen tijdens spraak gedeeltelijk hoorbaar blijven.

## Vereisten

- Windows 10/11 x64;
- .NET 9 Desktop Runtime voor de kleinste framework-dependent build;
- een virtuele audiokabel voor gebruik als selecteerbare microfoon in andere
  apps;
- voor native rebuilds: Visual Studio 2022 C++ Build Tools.

## Bouwen en testen

```powershell
dotnet restore .\Muted.sln
dotnet build .\Muted.sln -c Release --no-restore
dotnet test .\Muted.sln -c Release --no-build
```

Start een developmentbuild met:

```powershell
dotnet run --project .\src\Muted.App\Muted.App.csproj -c Debug
```

Maak een kleine framework-dependent distributiemap:

```powershell
.\scripts\publish.ps1
```

Of bundel de .NET-runtime voor een grotere, zelfstandige map:

```powershell
.\scripts\publish.ps1 -SelfContained
```

Beide varianten komen onder `artifacts\Muted-win-x64`. De app is x64 omdat de
meegeleverde RNNoise-DLL x64 is. Het script maakt daarnaast
`artifacts\Muted-win-x64.zip`.

De lokale developmentbuild is niet digitaal ondertekend. Voor publieke
distributie horen zowel de EXE als een eventuele installer met een vertrouwd
code-signingcertificaat te worden ondertekend; anders kan SmartScreen een
waarschuwing tonen.

## Native RNNoise opnieuw bouwen

De gecontroleerde runtime-DLL staat al in de repository. Een reproduceerbare
rebuild downloadt exact de gepinde Xiph-bron en het bijbehorende model,
verifieert SHA-256-hashes, compileert baseline/SSE4.1/AVX2-code en voert een
native smoke test uit:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\native\rnnoise\build.ps1
```

Zie [native/rnnoise/README.md](native/rnnoise/README.md) voor pins, exports en
builddetails.

## Projectindeling

```text
src/Muted.Core           allocatievrije DSP-primitieven en instellingen
src/Muted.Audio.Windows  WASAPI-engine, apparaatcatalogus en RNNoise-wrapper
src/Muted.App            WPF-UI, tray, autostart en settings
native/rnnoise           reproduceerbare officiële native build
tests                    unit- en native smoke tests
```

De reden voor de virtuele-kabelarchitectuur en het productiepad naar een eigen
driver staan in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Licenties

De projectcode is nog niet onder een eigen distributielicentie geplaatst.
RNNoise (BSD-3-Clause) en NAudio (MIT) mogen wel worden meegedistribueerd zolang
hun notices behouden blijven. Zie [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
en de map `licenses`.
