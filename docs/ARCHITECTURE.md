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

Er worden in de capturecallback en per DSP-frame geen managed objecten
gealloceerd. De UI leest alleen atomaire metingen op 10 Hz en pauzeert die timer
wanneer het venster niet zichtbaar is.

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
