# Virtuele audiokabel instellen

> Nederlandse versie. De Engelse [VIRTUAL-CABLE.md](../VIRTUAL-CABLE.md) is leidend.

Een gewone Windows-app kan geen nieuw microfoonendpoint publiceren. Muted stuurt
het opgeschoonde signaal daarom naar de afspeelkant van een geïnstalleerde,
gesigneerde virtuele kabel. Je call-app gebruikt de gekoppelde opnamekant.

## VB-CABLE

1. Download het actuele pakket via de officiële
   [VB-CABLE-pagina](https://vb-audio.com/Cable/).
2. Pak het volledige archief uit en start de x64-setup als administrator.
3. Herstart Windows als de installer daarom vraagt.
4. Kies in Muted je fysieke microfoon bij **Microphone**.
5. Kies `CABLE Input` of `CABLE In 16ch` bij **Cable output**.
6. Start Muted met de grote knop.
7. Kies in Discord, Teams, Zoom of je game `CABLE Output` of `CABLE Out 16ch`
   als microfoon. Laat de normale call-uitgang op je headset of speakers staan.

Stel beide kabeluiteinden bij voorkeur in Windows in op 48.000 Hz. Zet de noise
suppression van de call-app eerst uit om dubbele filtering te vermijden. Hoort
de andere kant zichzelf terug via je speakers, zet dan **Echo cancellation** aan
en kies de uitgang waarop je echt luistert.

## Problemen oplossen

- Controleer **Instellingen > Privacy en beveiliging > Microfoon** en sta
  microfoontoegang voor desktop-apps toe.
- Start de call-app opnieuw als de kabel pas na die app is geïnstalleerd.
- Muted start alleen met een herkende virtuele uitgang; dit voorkomt feedback
  wanneer een opgeslagen kabel ontbreekt en Windows naar speakers terugvalt.
- Instellingen staan in `%LOCALAPPDATA%\Muted\settings.json`; verwijder dit
  bestand terwijl Muted afgesloten is om de configuratie te resetten.
- Diagnostiek staat in `%LOCALAPPDATA%\Muted\Muted.log`.

De kabeldriver wordt niet met Muted gebundeld en valt onder de licentievoorwaarden
van de leverancier.
