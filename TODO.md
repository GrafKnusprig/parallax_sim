# Texturen

- [x] texturen für alle planeten / monde
- [x] erde hat spiegelverkehrte textur ggf. bei allen anderen auch prüfen
- [x] saturn ringe tilt fixen
- [x] high res textuen für alle planeten: https://www.solarsystemscope.com/textures/
- [ ] atmosphäre und bump maps für planeten im shader hinzufügen falls verfügbar im  HighResTextures folder
- [ ] particle sun wäre echt sehr geil: https://www.youtube.com/watch?v=qJEBAPRt8AA

# UI

- [ ] label aufhübschen
- [ ] label sollten nicht durch planeten angezeigt werden
- [x] label sollte nicht angezeigt werden wenn man direkt vor dem planeten ist

## VR

- [x] autopilot vr-ui (akutell kann man nicht damit interagieren)
- [ ] planet-info für vr verbessern
- [ ] put important information in the center (consider fov properties from lecture)
- [ ] Use the periphiral vision for subtle effects (light speed animation, planet approximation warning, red blinking etc.)

## Neue Features

- [ ] journey-funktion. fliegt automatisch eine vorprogrammierte route von planet zu planet. große schrift: nächstes ziel "jupiter". bei ankuft wird einmal um den planeten geflogen und infos eingeblendet. danach fliegt man weiter zum nächsten.

- [ ] controller steuerung
- [ ] z achse über q und e steuern -> auf vr controller mappen

- [ ] ladescreen -> random planet dreht sich davor/dahinter

- [ ] VR: viewport des HMD sollte auf den angepeilten planeten drehen (autopilot)

- [ ] neues datenset für info tool bzw. aktuell daten aus dem neuen datenset lesen

- [ ] future: google maps auf erde
- [ ] future: startbildschirm mit settings etc. rando planet dreht sich im hintergrund

# Bugs

- [ ] BUG: proxima labels werden immer alle angezeigt. es reicht wenn auf große distanz nur eines davon angezeigt wird.
- [x] BUG: autopilot beendet nicht
- [x] BUG: autopilot ist teilweise viel zu langsam wenn ein planet in der nähe ist -> erste speed zonen überspringen
- [x] BUG: info tool lässt sich für saturn nicht anzeigen da dione der nächste ist (nach dem autopilot travel)
- [ ] BUG: in den jupiter kann man "reinfliegen" die geschwindigkeits regulierund und scaling scheint einen fehler zu haben -> passiert, wenn als nearest planet ein mond erfasst wird und nicht der planet. vor allem bei saturn und jupiter. berechnung für nearest planet überprüfen. evtl von oberfläche aus berechnen, nicht von planeten mittelpunkt.
- [ ] Parallaxen Genauigkeit verbessern! ruckelt für weit entfernte objekte

# Refactoring

- [x] UI in eigenes file über interface oder anders exposen
- [-] Autopilot in eigenes file wenn möglich (könnte einfacher für die journey erweitert werden) -> UI is ausgelagert rest da sehr an die entfernungsberechnung gebunden bleibt im parallaxmanager