# Attribution technique des usages

Provider est l'identité du fournisseur tarifaire, avec la même signification que
dbo.AiPricing.Provider. La convention validée pour les déploiements Claude actuels
est Anthropic ; Azure/Foundry désigne leur canal de déploiement et de facturation.

Les trois mappings réels sont définis une seule fois dans WebApp.Api/appsettings.json,
section AiPricingIdentity:Deployments. infra/main-app.bicep lit cette même section
pour injecter les neuf variables Container Apps au prochain déploiement.
Le backend charge également ce fichier en local et dans l'image publiée.
Aucun tarif monétaire n'y figure. Les exemples ci-dessous illustrent l'extension
à d'autres agents ; ils ne doivent pas remplacer les mappings réels.

Configurer cette section via les sources IConfiguration existantes (appsettings
ou variables d'environnement). Exemple fictif, à remplacer par des valeurs vérifiées :

```json
{
  "AiPricingIdentity": {
    "Agents": [
      {
        "ProjectEndpoint": "https://example.invalid/api/projects/example",
        "AgentId": "machine-agent",
        "AgentVersion": "3",
        "Provider": "verified-tariff-provider",
        "Deployment": "verified-deployment"
      }
    ],
    "Deployments": [
      {
        "ProjectEndpoint": "https://example.invalid/api/projects/example",
        "Deployment": "verified-deployment",
        "Provider": "verified-tariff-provider"
      }
    ]
  }
}
```

Les trois clés sont comparées exactement, avec distinction de casse, sans
normalisation de l'URL ni correspondance partielle. Le projet fait partie de la
clé car un nom/version d'agent peut exister dans plusieurs projets.
Le SDK documente DeclarativeAgentDefinition.Model comme le déploiement du modèle.
Le mapping exact ProjectEndpoint + Deployment est donc prioritaire. En son absence,
le resolver essaie ProjectEndpoint + AgentId + AgentVersion. Aucun fallback sur
le nom de modèle retourné par la réponse, ni sur son préfixe.
Un mapping de déploiement ambigu ou invalide ne déclenche pas de repli.
Un Deployment configuré pour un agent qui contredit le déploiement SDK invalide ce mapping.
Provider doit être non vide et limité à 100 caractères ; Deployment, si fourni,
doit être non vide et limité à 200 caractères.

Une correspondance absente, invalide ou ambiguë produit Provider NULL et un
warning structuré, sans empêcher l'appel. Le Deployment SDK reste conservé s'il
est disponible et compatible avec la taille SQL ; sinon il reste NULL.
L'identité est résolue avant l'appel, puis conservée pour tous ses événements.
Le résumé utilise le projet, l'agent et la version globaux, indépendamment de la machine.
Vision conserve les valeurs extraites de sa sortie sans passer par ce mapping.

Les valeurs d'un mapping doivent être vérifiées et rester historiquement exactes
pour la version concernée. Les lignes SQL existantes ne sont jamais recalculées.
Appliquer la migration AddAiUsagePricingIdentity avant de déployer ce code :
le repository attend les deux nouvelles colonnes. Cette tâche ne l'applique pas.
