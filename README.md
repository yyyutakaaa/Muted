# Muted

Muted is een kleine Windows-app die je microfoon door [RNNoise](https://github.com/xiph/rnnoise)
haalt, lokaal, voordat Discord, Teams of je game het signaal te horen krijgt.
Ventilatorlawaai, mic-hiss en toetsenbordgeklepper worden er grotendeels
uitgefilterd.

```text
fysieke microfoon
  → WASAPI shared mode (48 kHz mono)
  → RNNoise (480 samples per call, 20 ms vertraging)
  → optionele voice gate
  → driftcorrectie
  → afspeelkant van een virtuele audiokabel
  → opnamekant van die kabel in Discord / call-app / game
```

## Eerst gebruiken

Windows laat een app alleen een nieuwe, selecteerbare microfoon publiceren via
een gesigneerde audiodriver, en dat is niet iets wat Muted zelf meelevert.
Daarom leunt de app op een virtuele kabel die je al hebt geïnstalleerd, zoals
[VB-CABLE](https://vb-audio.com/Cable/).

1. Installeer een gesigneerde virtuele audiokabel en herstart Windows als de
   installer daarom vraagt.
2. Start Muted.
3. Kies je echte microfoon bij **Microfoon**.
4. Kies de afspeelkant van de kabel bij **Uitgang naar virtuele kabel** — bij
   VB-CABLE meestal `CABLE Input` of `CABLE In 16ch`.
5. Druk op de aan/uitknop.
6. Kies in Discord, Teams, Zoom of je game de opnamekant van de kabel als
   microfoon — bij VB-CABLE meestal `CABLE Output` of `CABLE Out 16ch`.

Muted start bewust niet naar gewone speakers of een headset. Zonder die
beperking zou een ontbrekende kabel bij autostart onopgemerkt feedback in je
microfoon kunnen veroorzaken. Volledige installatie, de 48 kHz-instelling en
troubleshooting staan in [docs/VIRTUAL-CABLE.md](docs/VIRTUAL-CABLE.md).

Zet de ingebouwde noise suppression van je call-app liever uit als je pompen
of vervorming hoort — twee suppressors achter elkaar werken niet altijd beter
dan één.

## Wat het doet

- draait de officiële, vastgepinde Xiph RNNoise full-model build;
- vangt audio op via 48 kHz mono WASAPI en rendert event-driven;
- heeft een live dry/wet-regeling met correct vertraagd dry-pad;
- kan optioneel RNNoise's eigen VAD gebruiken om stiltes extra dicht te maken;
- corrigeert klokdrift sample voor sample tijdens lange sessies;
- toont input/output-meters, ververst apparaten automatisch bij hotplug, en
  kan naar de tray minimaliseren met autostart;
- bewaart instellingen in `%LOCALAPPDATA%\Muted\settings.json` en logt fouten
  naar `%LOCALAPPDATA%\Muted\Muted.log`;
- heeft geen account, geen cloud, geen telemetrie en neemt niets op.

RNNoise onderdrukt ruis, het cancelt geen echo. Geluid dat via je speakers
weer je microfoon binnenkomt, vraagt om AEC of gewoon een headset. Harde
toetsenbordklikken tijdens spraak blijven soms deels hoorbaar — dat is een
grens van het model, niet een bug.

## Vereisten

- Windows 10/11 x64;
- .NET 9 Desktop Runtime voor de kleinste build;
- een virtuele audiokabel, zodat andere apps Muted's output als microfoon
  kunnen kiezen;
- Visual Studio 2022 C++ Build Tools, alleen nodig als je de native RNNoise-DLL
  zelf herbouwt.

## Bouwen en testen

```powershell
dotnet restore .\Muted.sln
dotnet build .\Muted.sln -c Release --no-restore
dotnet test .\Muted.sln -c Release --no-build
```

Developmentbuild starten:

```powershell
dotnet run --project .\src\Muted.App\Muted.App.csproj -c Debug
```

Kleine, framework-dependent distributiemap maken:

```powershell
.\scripts\publish.ps1
```

Of met gebundelde .NET-runtime, groter maar zelfstandig:

```powershell
.\scripts\publish.ps1 -SelfContained
```

Beide landen onder `artifacts\Muted-win-x64`, plus een
`artifacts\Muted-win-x64.zip`. De app is x64-only omdat de meegeleverde
RNNoise-DLL dat ook is.

Een lokale build is niet digitaal ondertekend. Voor publieke distributie
horen de EXE en een eventuele installer ondertekend te zijn met een vertrouwd
code-signingcertificaat, anders waarschuwt SmartScreen.

## Native RNNoise opnieuw bouwen

De geverifieerde runtime-DLL staat al in de repo, dus dit is alleen nodig als
je zelf wilt controleren dat de build reproduceerbaar is. Het script haalt de
exacte gepinde Xiph-bron en het model op, checkt SHA-256-hashes, compileert
baseline/SSE4.1/AVX2-varianten en draait een native smoke test:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\native\rnnoise\build.ps1
```

Pins, exports en builddetails staan in
[native/rnnoise/README.md](native/rnnoise/README.md).

## Projectindeling

```text
src/Muted.Core           allocatievrije DSP-primitieven en instellingen
src/Muted.Audio.Windows  WASAPI-engine, apparaatcatalogus en RNNoise-wrapper
src/Muted.App            WPF-UI, tray, autostart en settings
native/rnnoise           reproduceerbare officiële native build
tests                    unit- en native smoke tests
```

Waarom de virtuele-kabelroute de eerste versie is, en wat het pad naar een
eigen driver zou betekenen, staat in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Licentie

Muted zelf valt onder de MIT-licentie, zie [LICENSE](LICENSE). RNNoise
(BSD-3-Clause) en NAudio (MIT) worden meegedistribueerd met behoud van hun
eigen notices — zie [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) en de map
`licenses`.
