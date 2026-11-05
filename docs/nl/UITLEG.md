# Muted: wat doet de app?

Muted is een kleine Windows-app die je microfoon opschoont voordat Discord,
Teams, Zoom of een game het geluid hoort. Alles gebeurt lokaal op je eigen pc:
geen account, geen cloud en geen telemetrie.

## In één zin

Muted haalt achtergrondgeluid (ventilator, ruis, toetsenbord) uit je stem en
stuurt de schone stem door naar je call-app.

## Hoe het werkt

Windows laat een gewone app geen nieuwe microfoon aanmaken. Daarom gebruikt
Muted een **virtuele audiokabel** (bijvoorbeeld VB-CABLE):

```text
je echte microfoon
  → Muted (ruis weg, volume, gate, limiter)
  → virtuele kabel (afspeelkant)
  → virtuele kabel (opnamekant) = "microfoon" in Discord, Teams of je game
```

In je call-app kies je dus niet je echte microfoon, maar de opnamekant van de
kabel (meestal `CABLE Output`). Zie [VIRTUAL-CABLE.md](VIRTUAL-CABLE.md) voor de
installatie.

Muted weigert te starten naar gewone speakers of een headset. Zo komt je eigen
geluid nooit per ongeluk terug in je microfoon.

## Wat de app voor je doet

### Geluid opschonen
- **Ruisonderdrukking** met RNNoise, een bekend, open model van Xiph. Het
  filtert ventilator, sis en toetsenbordgeklik. Harde klikken tijdens het
  praten kunnen er deels nog doorheen komen; dat is een grens van het model.
- **Voice gate**: stil als je niet praat, met een instelbare hold-tijd.
- **High-pass filter** tegen gerommel van je bureau.

### Volume onder controle
- **Input- en outputgain** (microfoonboost).
- **Automatisch niveau**: houdt je stem rustig op een gelijk volume.
- **Limiter**: vangt pieken af in plaats van te laten clippen.

### Warme radio-stem
- **Warm radio voice**: een volle, dichtbij klinkende en zachte stem, als een
  radiopresentator. Vijf schuifregelaars: warmte, nabijheid, zachtheid (met
  de-esser voor scherpe s-klanken), compressie en saturatie.
- Preset **Warm Radio** zet alles in één klik goed.

### Stem afstellen
- **Tune to my voice**: een begeleide meting van 24 seconden. Eerst even stil
  zijn, dan een zin voorlezen, dan één keer iets hard zeggen. Muted meet je
  stem en de ruimte en zet daarna zelf de microfoonboost, de voice gate, de
  high-pass, de schreeuwgrens en de warme-stem-regelaars goed. Je hoort het
  meteen en kiest **Keep** of **Undo**. Alles blijft op je eigen pc.
- **Keep tuning while I talk**: stelt tijdens het praten langzaam de
  microfoonboost en de voice gate bij, bijvoorbeeld als je verder van de mic
  gaat zitten of er een ventilator aanslaat. Kleine stappen, nooit ver van je
  startpunt, en de schreeuwbeveiliging en warme stem blijven onaangeroerd.

### Schreeuwbeveiliging
- **Shout guard**: wordt je te luid, dan hoort de ander niets meer (of je komt
  20 dB zachter door, als je dat kiest). Muted kijkt 20 ms vooruit, dus ook het
  begin van een schreeuw wordt opgevangen.
- Stel de grens in een paar dB boven je luidste normale spraak; het
  inputniveau staat er direct naast. Presets zetten de beveiliging nooit uit.

### Zonder headset praten
- **Echo-onderdrukking**: Muted luistert mee met wat je speakers afspelen en
  haalt dat uit je microfoon. De ander hoort zichzelf dan niet terug.

### Bediening
- **Sneltoetsen** voor push-to-talk, push-to-mute, mute, ruisonderdrukking en
  start/stop. Ze werken ook in volledig scherm games en nemen geen toetsaanslag
  weg.
- **Tray-icoon** met status (praten, gedempt, gestopt, fout) en snelle knoppen.
- **Compact paneel** dat altijd bovenop blijft, met meter en mute-knop.
- **Lichte en donkere modus** in één violet kleurenpalet.

### Profielen en presets
- Presets: **Natural, Gaming, Podcast, Meeting, Warm Radio**.
- Eigen profielen opslaan, wisselen vanuit de app of tray, en importeren of
  exporteren als JSON.

### Controleren en testen
- **Live golfvorm** en dBFS-meters met piekhouder en clip-indicator, plus een
  uitlezing van hoeveel ruis is verwijderd.
- **Monitoring**: hoor via een tweede uitgang wat de ander hoort.
- **Opnemen en terugluisteren** (maximaal twee minuten, in het geheugen) en
  exporteren als WAV.
- **Diagnose**: controleert kabelroute, samplefrequentie, RNNoise, microfoonsignaal
  en stabiliteit. Het resultaat is te kopiëren voor een bugmelding.
- **Gezondheidswaarschuwingen** bij te hoge belasting, oplopende buffer of
  haperingen, plus sessiestatistieken.

### Betrouwbaarheid
- Herstelt zichzelf als een apparaat tijdens een gesprek verdwijnt.
- Kan de standaardmicrofoon van Windows volgen.
- Corrigeert klokverschillen tussen microfoon en kabel tijdens lange sessies.
- Start minimaal in de tray, desgewenst met Windows.
- Controleert op updates en vraagt eerst toestemming.

## Eenvoudig als standaard

Op de Audio-pagina staan vier schakelaars: ruisonderdrukking, echo-onderdrukking,
voice gate en microfoonboost. Alle andere instellingen zitten achter
**Geavanceerd** in Instellingen en veranderen niets aan het geluid zolang je ze
niet aanpast.

## Wat je nodig hebt

- Windows 10 of 11 (x64)
- .NET 9 Desktop Runtime (bij de kleine build)
- Een gesigneerde virtuele audiokabel, zoals VB-CABLE

## Snel starten

1. Installeer de virtuele kabel en herstart Windows als dat gevraagd wordt.
2. Start Muted en kies je echte microfoon.
3. Kies bij **Uitgang naar virtuele kabel** de afspeelkant (`CABLE Input`).
4. Druk op de grote aan-knop.
5. Kies in Discord, Teams, Zoom of je game de opnamekant (`CABLE Output`) als
   microfoon.
6. Zet de ruisonderdrukking van de call-app uit, om dubbel filteren te vermijden.

## Waar staan je gegevens?

| Wat | Waar |
|---|---|
| Instellingen | `%LOCALAPPDATA%\Muted\settings.json` |
| Logbestand | `%LOCALAPPDATA%\Muted\Muted.log` |
| Opnames | alleen in het geheugen, tenzij je zelf een WAV opslaat |

## Meer lezen

- [README.md](../README.md): bouwen, testen en releases
- [ARCHITECTURE.md](ARCHITECTURE.md): technische opbouw van de audioketen
- [VIRTUAL-CABLE.md](VIRTUAL-CABLE.md): kabel instellen en problemen oplossen
