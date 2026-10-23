using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Source de vérité unique pour la liste des héros côté Home/Lobby.
/// Évite de dupliquer l'appel GET /api/v1/heroes entre le carrousel du Home
/// et l'écran Hero Collection : les deux s'abonnent à ce service.
///
/// Comportement :
/// - Charge la liste une fois au démarrage (cache en mémoire).
/// - Fallback MVP statique si le backend n'est pas joignable, pour garder
///   le Home utilisable pendant le développement front-only.
/// </summary>
public class HeroCatalogService : MonoBehaviour
{
    [Header("Backend")]
    [Tooltip("Base URL du HeroService. Laisser vide pour rester en mode MVP local.")]
    [SerializeField] private string heroServiceBaseUrl = string.Empty;

    private HeroApiClient apiClient;
    private HeroListItemDto[] cachedHeroes = Array.Empty<HeroListItemDto>();
    private bool hasLoaded;

    public IReadOnlyList<HeroListItemDto> CachedHeroes => cachedHeroes;
    public bool HasLoaded => hasLoaded;

    public event Action<HeroListItemDto[]> OnCatalogLoaded;

    private void Awake()
    {
        if (!string.IsNullOrWhiteSpace(heroServiceBaseUrl))
        {
            apiClient = new HeroApiClient(heroServiceBaseUrl);
        }
    }

    private async void Start()
    {
        await LoadCatalogAsync();
    }

    public async Task LoadCatalogAsync()
    {
        if (apiClient == null)
        {
            cachedHeroes = BuildFallbackCatalog();
            hasLoaded = true;
            Debug.Log("[HeroCatalogService] Aucune base URL configurée — catalogue MVP local utilisé.");
            OnCatalogLoaded?.Invoke(cachedHeroes);
            return;
        }

        try
        {
            cachedHeroes = await apiClient.GetHeroesAsync();

            if (cachedHeroes == null || cachedHeroes.Length == 0)
            {
                Debug.LogWarning("[HeroCatalogService] Backend a retourné une liste vide, fallback MVP local.");
                cachedHeroes = BuildFallbackCatalog();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[HeroCatalogService] Échec du chargement backend, fallback MVP local. {ex.Message}");
            cachedHeroes = BuildFallbackCatalog();
        }

        hasLoaded = true;
        OnCatalogLoaded?.Invoke(cachedHeroes);
    }

    private static HeroListItemDto[] BuildFallbackCatalog()
    {
        // MVP V1 : reflète temporairement la même liste statique que
        // HeroSelectionPanelPresenter, avec affiliation mythologique pour
        // que le carrousel et les filtres de pantheon aient de la donnée.
        return new[]
        {
            new HeroListItemDto
            {
                id = "hero_warrior",
                name = "Warrior",
                heroClass = new HeroClassDto { id = "class_fighter", name = "Fighter" },
                affiliation = new HeroAffiliationDto
                {
                    archetypeName = "Champion",
                    mythologyName = "Greek",
                    cultureName = "Hellenic"
                }
            },
            new HeroListItemDto
            {
                id = "hero_mage",
                name = "Mage",
                heroClass = new HeroClassDto { id = "class_mage", name = "Mage" },
                affiliation = new HeroAffiliationDto
                {
                    archetypeName = "Sorcerer",
                    mythologyName = "Norse",
                    cultureName = "Scandinavian"
                }
            },
            new HeroListItemDto
            {
                id = "hero_archer",
                name = "Archer",
                heroClass = new HeroClassDto { id = "class_marksman", name = "Marksman" },
                affiliation = new HeroAffiliationDto
                {
                    archetypeName = "Hunter",
                    mythologyName = "Egyptian",
                    cultureName = "Kemetic"
                }
            },
            new HeroListItemDto
            {
                id = "hero_tank",
                name = "Tank",
                heroClass = new HeroClassDto { id = "class_tank", name = "Tank" },
                affiliation = new HeroAffiliationDto
                {
                    archetypeName = "Guardian",
                    mythologyName = "Mesopotamian",
                    cultureName = "Sumerian"
                }
            },
            new HeroListItemDto
            {
                id = "hero_assassin",
                name = "Assassin",
                heroClass = new HeroClassDto { id = "class_assassin", name = "Assassin" },
                affiliation = new HeroAffiliationDto
                {
                    archetypeName = "Shadow",
                    mythologyName = "Japanese",
                    cultureName = "Shinto"
                }
            }
        };
    }
}
