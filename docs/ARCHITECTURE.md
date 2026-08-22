# Architectuur

## Waarom een virtuele kabel

Een desktop-app kan bestaande capture- en render-endpoints met WASAPI openen,
maar kan niet zelfstandig een nieuw microfoonendpoint publiceren. Discord en
games enumereren capture-endpoints die door audiodrivers worden aangeboden.
Een eigen “Muted Microphone” vraagt dus om een Windows WaveRT/AVStream-driver,
administratorinstallatie en Microsoft driver signing.

Daarom gebruikt de huidige werkende versie:

```text
microfoon capture endpoint
  → Muted user-mode DSP
  → virtual-cable render endpoint
  → gekoppeld virtual-cable capture endpoint
  → communicatie-app
```

Dit werkt met een bestaande, gesigneerde kabel en houdt RNNoise volledig in
user mode.

## Realtime pad

- WASAPI shared/event mode vraagt Windows om 48 kHz IEEE-float mono. Shared
  mode verzorgt endpointconversie wanneer de fysieke microfoon een ander
  hardwareformaat heeft.
- De capturecallback schrijft alleen naar een vaste SPSC-floatringbuffer.
- Een `AboveNormal` processingthread verwerkt exact 480 samples per call.
- Samples worden van `[-1, 1]` naar de door RNNoise gebruikte 16-bit
  amplitudeschaal geconverteerd en daarna teruggeschaald.
- Het dry-pad wordt 960 samples vertraagd voordat dry/wet-menging plaatsvindt.
  De vastgepinde RNNoise-build combineert een overlappend analysevenster met
  een expliciet vertraagd spectrum; een impulsmeting bevestigt deze 20 ms.
- Bij starten wordt de aangevraagde WASAPI-buffer plus 20 ms reserve gevuld.
  Daarna houdt de outputprovider een lage software-FIFO van 20 ms aan. Na een
  renderstall verwijdert de consumer oude backlog boven de high-watermark,
  zodat latency niet minutenlang blijft oplopen. Underruns worden met stilte
  gevuld en drop/underruncounters blijven meetbaar.
- Omdat fysieke capture en virtuele render verschillende klokken kunnen
  hebben, maakt de driftcorrector een frame incidenteel 479 of 481 samples
  lang via lineaire interpolatie.

## Verwerkingsketen

Per frame van 480 samples, in deze volgorde:

```text
inputgain → high-pass (optioneel) → dry-vertraging ∥ RNNoise
  → dry/wet-menging → voice gate → automatisch niveau (optioneel)
  → outputgain → limiter → mute → driftcorrectie → kabel
```

- De high-pass is een tweede-orde Butterworth vóór de vertraging, zodat dry en
  wet dezelfde filtering krijgen. Denormals worden per frame weggeschreven.
- Het automatische niveau meet RMS en past alleen aan wanneer RNNoise spraak
  meldt; de gain loopt binnen het frame op naar de nieuwe waarde, zonder zipper.
- De limiter bepaalt eerst de piek van het hele frame en daarmee de benodigde
  gain, dus het plafond van 0,97 wordt nooit overschreden. Een per-sample
  veiligheid dekt de aanlooptijd van 1,5 ms af.
- De reductiemeting vergelijkt de RMS voor en na RNNoise; dat is wat de UI als
  "noise removed" toont.
- Een optioneel tweede renderpad stuurt hetzelfde frame naar een monitoruitgang.
  Dat pad heeft zijn eigen klok en trimt zijn eigen backlog; valt het weg, dan
  blijft de kabel gewoon doorlopen en meldt de engine alleen `MonitorFaulted`.

Er worden in de capturecallback en per DSP-frame geen managed objecten
gealloceerd. De UI leest alleen atomaire metingen op 30 Hz en pauzeert die timer
wanneer geen venster zichtbaar is. De golfvorm in de UI leest een aparte
ringbuffer met één piekwaarde per frame.

## Globale sneltoetsen

`RegisterHotKey` ziet alleen keydown, terwijl push-to-talk ook keyup nodig
heeft. Daarom gebruikt Muted een `WH_KEYBOARD_LL`-hook op de UI-thread, die
alleen wordt geïnstalleerd zodra er een toets is toegewezen. De hook geeft
iedere toets door aan de rest van Windows, zodat een spel of gesprek de toets
gewoon blijft zien.

## Productiepad zonder externe kabel

Een zelfstandige distributie kan later een minimale, capture-only virtuele
microfoon toevoegen op basis van Microsofts SysVAD/WaveRT-voorbeeld. De driver
hoort alleen endpoint, klok en ringbuffertransport te leveren. Capture,
RNNoise, instellingen en modelupdates blijven in het bestaande user-mode
proces.

Zo'n driver is pas geschikt voor eindgebruikers na onder andere:

- afgeslankte en beveiligde private driverinterface;
- clocking-, underrun- en hersteltests;
- installer en uninstall/upgradepad;
- HLK/WHCP-validatie en Microsoft-signing;
- crash-, sleep/resume- en hotplugtests op meerdere Windowsversies.

Een generieke capture-APO is geen vervanging: moderne APO-distributie is aan
de onderliggende audiodriver/hardware-associatie gekoppeld en maakt bovendien
geen nieuw endpoint. WASAPI process loopback onderschept renderaudio, geen
fysieke microfoon.

Officiële referenties:

- [Microsoft SysVAD sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-driver-samples/sysvad-virtual-audio-device-driver-sample/)
- [Audio Processing Object architecture](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/audio-processing-object-architecture)
- [Low-latency audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/low-latency-audio)
- [Xiph RNNoise](https://github.com/xiph/rnnoise)
