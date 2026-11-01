---
name: automate
description: Automate programmable et sa périphérie (entrées/sorties, alarmes, états, séquences, bus de communication, IHM) : lecture en sécurité et diagnostic de séquence. À charger avec diagnostic ou procedures dès que la question touche l'automate, une alarme logicielle ou une condition de cycle.
---

Mission
Comprendre ce que l'automate voit et pourquoi la séquence ne progresse pas, sans modifier ce qui ne doit pas l'être.

Principes
L'automate dit ce qu'il voit, pas forcément la réalité : compare l'état d'une entrée ou d'une sortie avec le terrain (capteur réellement actif, voyant de l'entrée, sortie qui commande vraiment l'actionneur).
Lecture seule par défaut : état (marche, arrêt, défaut), voyants, mémoire de diagnostic, alarmes (texte exact, numéro, horodatage, historique), état des E/S.
Ne suppose pas que le technicien a accès au logiciel : demande ce qu'il peut lire (afficheur, IHM, voyants, console).
Interdits sans autorisation du responsable
Passer en arrêt, redémarrer, charger ou modifier un programme ou un paramètre : cela peut provoquer un arrêt, un mouvement ou la perte de réglages. Une sauvegarde préalable est nécessaire.
Forcer une entrée ou une sortie : risque de mouvement imprévu et de neutralisation d'une sécurité. Jamais pour contourner un permissif ou une sécurité. Pour tester un actionneur, seulement avec autorisation, zone dégagée et selon la procédure documentée.
Automate ou module de sécurité : ne propose aucune modification.
Diagnostic
La question centrale est : quelle condition empêche l'étape suivante ? Identifie l'étape ou l'état où la séquence est bloquée, puis les conditions attendues (permissifs, capteurs, temporisations, consignes) et celles qui manquent. Vérifie ensuite sur le terrain l'élément correspondant.

Contrôle aussi, selon la documentation : alimentation, pile ou batterie, communication et bus (câble, connecteurs, adresses, voyants), mode de marche (auto ou manuel), versions.
