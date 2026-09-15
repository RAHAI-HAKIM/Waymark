"""One-shot enrichment: docs/products.csv -> the grocery-dz catalogue (W10, D-046).

Hakim's list names 399 real Algerian products and nothing else. The generator needs a
subcategory with a VAT class, a size, prices, a supplier, a pack size, a shelf life, a
demand rate, a seasonality profile and a substitutability tier for every one of them. This
script adds those columns from the family rules below, with seeded jitter, and writes:

    catalogue.csv      one row per variant, prices at commissioning
    categories.csv     the 8 categories and their subcategories, each with a VAT class
    suppliers.csv      the 5 suppliers, delivery days and stated lead times
    price_changes.csv  dated retail and purchase price changes during 2025

Every value it adds is `source: guess`. The rules are here, at the top, so a reviewer reads
the reasoning rather than 399 numbers. Run once; after that, edit the CSVs directly.
Re-running overwrites those edits.

Deterministic: the same input and SEED give byte-identical output. Only random.Random
seeded with a string is used, which Python keeps stable across versions.

    python tools/enrich-catalogue/enrich.py
"""
import csv
import math
import os
import random
import re
from datetime import date, timedelta

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SOURCE = os.path.join(ROOT, "docs", "products.csv")
OUT = os.path.join(ROOT, "src", "Waymark.Generator", "inputs", "catalogues", "grocery-dz")

SEED = "waymark-grocery-dz-v1"

# Total base demand across the catalogue, in units per day. Each variant's rate is scaled so
# the catalogue sums to this. With the configured baskets (mean 7.2 units, Hakim 14/09) that
# is about 58 baskets a day; the basket count follows from this figure, not the reverse.
TARGET_UNITS_PER_DAY = 420.0

# In-store (restricted circulation) GS1 prefix, so a synthetic barcode can never scan as a
# real product on a real till.
BARCODE_PREFIX = "200"

PRICE_CHANGE_WINDOW = (date(2025, 2, 1), date(2025, 11, 30))

# Most shelf prices sit on the 5 DZD cash step, so cash never needs rounding. Hakim (14/09)
# wants tender rounding exercised: this share of non-regulated variants carries 1–4 DZD on
# top of every retail price it has, commissioning and changes alike. Always upward, so a
# margin never turns into a loss; drawn from its own stream, so no other value moves.
OFF_STEP_SHARE = 0.25

# ---------------------------------------------------------------------------------------
# Categories. The schema holds VAT only on categories.tax_rate, so the VAT class is carried
# by a subcategory under Hakim's top category. 900 = 9%, 1900 = 19%.
# ---------------------------------------------------------------------------------------
SUBCATEGORIES = {
    "Laits & Lben": ("Produits Laitiers & Crémerie", 900),
    "Yaourts & Crèmes": ("Produits Laitiers & Crémerie", 900),
    "Fromages": ("Produits Laitiers & Crémerie", 1900),
    "Margarine & Smen": ("Produits Laitiers & Crémerie", 900),
    "Pâtes & Couscous": ("Épicerie Salée", 900),
    "Semoule & Farine": ("Épicerie Salée", 900),
    "Huiles": ("Épicerie Salée", 900),
    "Légumes Secs": ("Épicerie Salée", 900),
    "Biscuits & Gaufrettes": ("Épicerie Sucrée & Biscuiterie", 1900),
    "Chocolat & Pâtes à Tartiner": ("Épicerie Sucrée & Biscuiterie", 1900),
    "Halwa & Confitures": ("Épicerie Sucrée & Biscuiterie", 1900),
    "Chips & Snacks": ("Épicerie Sucrée & Biscuiterie", 1900),
    "Eaux Minérales": ("Boissons & Jus", 900),
    "Sodas & Boissons Gazeuses": ("Boissons & Jus", 1900),
    "Jus & Nectars": ("Boissons & Jus", 1900),
    "Boissons Énergisantes": ("Boissons & Jus", 1900),
    "Sirops & Eaux Florales": ("Boissons & Jus", 1900),
    "Tomate & Harissa": ("Conserves & Condiments", 900),
    "Sauces & Condiments": ("Conserves & Condiments", 1900),
    "Thon & Poisson": ("Conserves & Condiments", 1900),
    "Olives, Pickles & Vinaigres": ("Conserves & Condiments", 1900),
    "Épices": ("Conserves & Condiments", 900),
    "Café": ("Café, Thé & Petit Déjeuner", 1900),
    "Thé & Infusions": ("Café, Thé & Petit Déjeuner", 1900),
    "Sucre & Édulcorants": ("Café, Thé & Petit Déjeuner", 900),
    "Miel & Sirop de Glucose": ("Café, Thé & Petit Déjeuner", 1900),
    "Céréales & Poudres Chocolatées": ("Café, Thé & Petit Déjeuner", 1900),
    "Alimentation Bébé": ("Café, Thé & Petit Déjeuner", 900),
    "Soins Cheveux": ("Hygiène & Soins Personnels", 1900),
    "Savons & Gels Douche": ("Hygiène & Soins Personnels", 1900),
    "Hygiène Bucco-Dentaire": ("Hygiène & Soins Personnels", 1900),
    "Déodorants": ("Hygiène & Soins Personnels", 1900),
    "Hygiène Bébé": ("Hygiène & Soins Personnels", 1900),
    "Protections Féminines": ("Hygiène & Soins Personnels", 1900),
    "Papier & Emballage": ("Hygiène & Soins Personnels", 1900),
    "Lessives": ("Détergents & Produits d'Entretien", 1900),
    "Vaisselle": ("Détergents & Produits d'Entretien", 1900),
    "Javel & Nettoyants": ("Détergents & Produits d'Entretien", 1900),
    "Accessoires Ménagers": ("Détergents & Produits d'Entretien", 1900),
    "Désodorisants": ("Détergents & Produits d'Entretien", 1900),
    "Insecticides": ("Détergents & Produits d'Entretien", 1900),
}

# Hakim's file has one truncated category name.
CATEGORY_FIXES = {"Détergents & Produits d me": "Détergents & Produits d'Entretien"}

# ---------------------------------------------------------------------------------------
# Suppliers: code, name, delivery weekdays, stated lead time (days), payment terms (days).
# ---------------------------------------------------------------------------------------
SUPPLIERS = [
    ("SUP-LAIT", "Laiterie & Frais Distribution", "sun|mon|tue|wed|thu|sat", 1, 0),
    ("SUP-CEVI", "Cevital Distribution Régionale", "sun|wed", 2, 15),
    ("SUP-EPIC", "Grossiste Épicerie Générale", "mon|thu", 2, 7),
    ("SUP-BOIS", "Boissons & Biscuiterie Distribution", "tue|sat", 1, 0),
    ("SUP-DROG", "Droguerie & Hygiène Distribution", "wed", 3, 15),
]

# Margin on the net (HT) retail price, as a (min, max) fraction. Regulated staples are thin.
MARGINS = {
    "regulated": (0.05, 0.08),
    "dairy": (0.12, 0.18),
    "dry": (0.10, 0.15),
    "drinks": (0.15, 0.22),
    "sweet": (0.20, 0.30),
    "coffee": (0.15, 0.22),
    "baby": (0.10, 0.14),
    "hygiene": (0.20, 0.30),
    "cleaning": (0.18, 0.26),
}

# ---------------------------------------------------------------------------------------
# Families. One per product line; every product maps to exactly one.
#
#   sub        subcategory (VAT class)          sup     supplier code
#   net        reference size, (amount, unit)   price   DZD TTC at that size, early 2025
#   shelf      shelf life in days, None = non-perishable
#   rate       units/day of the reference size before normalisation
#   profile    seasonality profile name (defined in inputs/configs/grocery-dz.json)
#   pack       units per carton at the reference size
#   tier       substitutability: 1 easily substituted, 3 brand-loyal
#   margin     key into MARGINS                 regulated  fixed prices, never change
#   prefer     "count" to read "40p" before "30L" (bin bags)
#   overrides  (variant regex, dict) — any of net/price anchor, rate_mult, profile, sup
#
# Prices scale with size: to the power 0.88 above the reference size (bulk is cheaper per
# gram) and 0.6 below it (a small pack carries its packaging). Rates scale with size to the
# power -0.6 above the reference and -0.35 below it, clamped to [0.2, 1.8]. Prices get ±6%
# jitter; rates get log-normal jitter (sigma 0.35) capped to [0.5, 2.0].
# ---------------------------------------------------------------------------------------
F = {
    "laits_uht": dict(sub="Laits & Lben", sup="SUP-LAIT", net=(1000, "ml"), price=140, shelf=120,
                      rate=5.0, profile="dairy_stable", pack=12, tier=2, margin="dairy"),
    "lben": dict(sub="Laits & Lben", sup="SUP-LAIT", net=(1000, "ml"), price=90, shelf=10,
                 rate=4.0, profile="ramadan_fresh", pack=12, tier=2, margin="dairy",
                 overrides=[(r"Bouteille", dict(price=110))]),
    "yaourt": dict(sub="Yaourts & Crèmes", sup="SUP-LAIT", net=(100, "g"), price=28, shelf=25,
                   rate=2.5, profile="dairy_fresh", pack=24, tier=1, margin="dairy"),
    "creme": dict(sub="Yaourts & Crèmes", sup="SUP-LAIT", net=(200, "ml"), price=130, shelf=30,
                  rate=1.0, profile="ramadan_cooking", pack=12, tier=2, margin="dairy"),
    "fromage": dict(sub="Fromages", sup="SUP-LAIT", net=(240, "g"), price=290, shelf=180,
                    rate=1.5, profile="dairy_stable", pack=24, tier=2, margin="dairy"),
    "margarine": dict(sub="Margarine & Smen", sup="SUP-EPIC", net=(250, "g"), price=130,
                      shelf=150, rate=1.8, profile="staples", pack=24, tier=2, margin="dairy"),
    "smen": dict(sub="Margarine & Smen", sup="SUP-EPIC", net=(1000, "g"), price=850, shelf=540,
                 rate=0.6, profile="ramadan_cooking", pack=12, tier=2, margin="dairy"),
    "pates": dict(sub="Pâtes & Couscous", sup="SUP-EPIC", net=(500, "g"), price=100, shelf=540,
                  rate=1.4, profile="staples", pack=20, tier=1, margin="dry",
                  overrides=[(r"Vermicelle|Cheveux d'Ange|Langue d'Oiseau|Tlitli",
                              dict(profile="ramadan_staples")),
                             (r"Cannelloni", dict(net=(250, "g"), price=180, rate_mult=0.3))]),
    "couscous": dict(sub="Pâtes & Couscous", sup="SUP-EPIC", net=(1000, "g"), price=200,
                     shelf=540, rate=1.3, profile="staples", pack=10, tier=1, margin="dry"),
    "semoule": dict(sub="Semoule & Farine", sup="SUP-EPIC", net=(1000, "g"), price=110,
                    shelf=270, rate=2.2, profile="ramadan_staples", pack=10, tier=1, margin="dry"),
    "farine": dict(sub="Semoule & Farine", sup="SUP-EPIC", net=(1000, "g"), price=95, shelf=270,
                   rate=1.6, profile="ramadan_baking", pack=10, tier=1, margin="dry"),
    # Regulated retail prices for table oil: 1L 125, 2L 250, 5L 650 DZD.
    "huile_table": dict(sub="Huiles", sup="SUP-EPIC", net=(1000, "ml"), price=125, shelf=365,
                        rate=3.2, profile="staples", pack=12, tier=1, margin="regulated",
                        regulated={1000: 125, 2000: 250, 5000: 650}),
    "huile_olive": dict(sub="Huiles", sup="SUP-CEVI", net=(1000, "ml"), price=1150, shelf=540,
                        rate=0.35, profile="ramadan_cooking", pack=12, tier=3, margin="dry"),
    "legumes_secs": dict(sub="Légumes Secs", sup="SUP-EPIC", net=(500, "g"), price=220, shelf=540,
                         rate=1.2, profile="ramadan_staples", pack=20, tier=1, margin="dry",
                         overrides=[(r"Pois Chiches", dict(price=260)),
                                    (r"Lentilles", dict(price=190))]),
    "biscuits": dict(sub="Biscuits & Gaufrettes", sup="SUP-BOIS", net=(100, "g"), price=70,
                     shelf=270, rate=2.2, profile="snacks", pack=24, tier=1, margin="sweet"),
    "gaufrettes": dict(sub="Biscuits & Gaufrettes", sup="SUP-BOIS", net=(120, "g"), price=100,
                       shelf=270, rate=1.6, profile="snacks", pack=24, tier=1, margin="sweet"),
    "pate_tartiner": dict(sub="Chocolat & Pâtes à Tartiner", sup="SUP-BOIS", net=(350, "g"),
                          price=720, shelf=365, rate=0.7, profile="sweets", pack=12, tier=3,
                          margin="sweet"),
    "chocolat": dict(sub="Chocolat & Pâtes à Tartiner", sup="SUP-BOIS", net=(100, "g"), price=200,
                     shelf=365, rate=1.1, profile="snacks", pack=24, tier=2, margin="sweet",
                     overrides=[(r"Kunafa|Makrout", dict(price=330, rate_mult=1.5))]),
    "halwa": dict(sub="Halwa & Confitures", sup="SUP-BOIS", net=(200, "g"), price=220, shelf=365,
                  rate=0.8, profile="ramadan_sweets", pack=24, tier=2, margin="sweet"),
    "confiture": dict(sub="Halwa & Confitures", sup="SUP-CEVI", net=(450, "g"), price=260,
                      shelf=540, rate=0.8, profile="staples", pack=12, tier=2, margin="sweet"),
    "chips": dict(sub="Chips & Snacks", sup="SUP-BOIS", net=(35, "g"), price=35, shelf=180,
                  rate=3.0, profile="snacks", pack=30, tier=1, margin="sweet"),
    "eau": dict(sub="Eaux Minérales", sup="SUP-BOIS", net=(1500, "ml"), price=45, shelf=540,
                rate=6.0, profile="water_summer", pack=6, tier=1, margin="drinks",
                overrides=[(r"Gazéifiée", dict(net=(1000, "ml"), price=70, rate_mult=0.3))]),
    "soda": dict(sub="Sodas & Boissons Gazeuses", sup="SUP-BOIS", net=(1000, "ml"), price=120,
                 shelf=270, rate=2.5, profile="beverages_summer", pack=12, tier=2,
                 margin="drinks"),
    "jus": dict(sub="Jus & Nectars", sup="SUP-BOIS", net=(1000, "ml"), price=190, shelf=270,
                rate=1.3, profile="juice", pack=12, tier=2, margin="drinks"),
    "energy": dict(sub="Boissons Énergisantes", sup="SUP-BOIS", net=(250, "ml"), price=120,
                   shelf=365, rate=0.8, profile="beverages_summer", pack=24, tier=3,
                   margin="drinks", overrides=[(r"Red Bull", dict(price=280, rate_mult=0.4))]),
    "sirops": dict(sub="Sirops & Eaux Florales", sup="SUP-BOIS", net=(750, "ml"), price=360,
                   shelf=540, rate=0.45, profile="ramadan_sweets", pack=12, tier=2,
                   margin="drinks",
                   overrides=[(r"Fleur d'Oranger|Eau de Rose", dict(net=(500, "ml"), price=190))]),
    "tomate": dict(sub="Tomate & Harissa", sup="SUP-EPIC", net=(400, "g"), price=150, shelf=730,
                   rate=2.8, profile="ramadan_staples", pack=24, tier=1, margin="dry"),
    "harissa": dict(sub="Tomate & Harissa", sup="SUP-EPIC", net=(135, "g"), price=90, shelf=540,
                    rate=1.0, profile="ramadan_cooking", pack=24, tier=2, margin="dry"),
    "sauces": dict(sub="Sauces & Condiments", sup="SUP-EPIC", net=(300, "g"), price=200,
                   shelf=270, rate=0.7, profile="sauces", pack=12, tier=2, margin="sweet"),
    "thon": dict(sub="Thon & Poisson", sup="SUP-EPIC", net=(160, "g"), price=380, shelf=1095,
                 rate=1.3, profile="ramadan_cooking", pack=24, tier=2, margin="dry"),
    "olives": dict(sub="Olives, Pickles & Vinaigres", sup="SUP-EPIC", net=(370, "g"), price=270,
                   shelf=540, rate=0.6, profile="ramadan_cooking", pack=12, tier=2, margin="dry"),
    "vinaigre": dict(sub="Olives, Pickles & Vinaigres", sup="SUP-EPIC", net=(1000, "ml"),
                     price=100, shelf=1095, rate=0.5, profile="staples", pack=12, tier=1,
                     margin="dry", overrides=[(r"Cidre", dict(price=380))]),
    "epices": dict(sub="Épices", sup="SUP-EPIC", net=(100, "g"), price=150, shelf=540, rate=0.8,
                   profile="spices", pack=20, tier=1, margin="dry",
                   overrides=[(r"Poivre", dict(price=260)), (r"Cumin", dict(price=200))]),
    "cafe_moulu": dict(sub="Café", sup="SUP-EPIC", net=(250, "g"), price=460, shelf=365, rate=2.2,
                       profile="coffee_tea", pack=20, tier=3, margin="coffee"),
    "cafe_soluble": dict(sub="Café", sup="SUP-EPIC", net=(100, "g"), price=760, shelf=540,
                         rate=0.4, profile="coffee_tea", pack=12, tier=3, margin="coffee",
                         overrides=[(r"Stick", dict(net=(18, "g"), price=40, rate_mult=0.8))]),
    "the_vert": dict(sub="Thé & Infusions", sup="SUP-EPIC", net=(250, "g"), price=320, shelf=730,
                     rate=0.9, profile="coffee_tea", pack=20, tier=3, margin="coffee"),
    "the_sachets": dict(sub="Thé & Infusions", sup="SUP-EPIC", net=(25, "pcs"), price=190,
                        shelf=730, rate=0.5, profile="coffee_tea", pack=24, tier=2,
                        margin="coffee", overrides=[(r"Infusion", dict(net=(20, "pcs"), price=150))]),
    # Regulated retail sugar: 1kg 95, 2kg 185, 5kg 450 DZD.
    "sucre": dict(sub="Sucre & Édulcorants", sup="SUP-CEVI", net=(1000, "g"), price=95,
                  shelf=1095, rate=4.5, profile="ramadan_staples", pack=10, tier=1,
                  margin="regulated", regulated={1000: 95, 2000: 185, 5000: 450}),
    "sucre_autres": dict(sub="Sucre & Édulcorants", sup="SUP-CEVI", net=(500, "g"), price=140,
                         shelf=1095, rate=0.6, profile="coffee_tea", pack=20, tier=2,
                         margin="dry",
                         overrides=[(r"Substitut", dict(net=(100, "g"), price=250, sup="SUP-EPIC"))]),
    "miel": dict(sub="Miel & Sirop de Glucose", sup="SUP-EPIC", net=(500, "g"), price=1300,
                 shelf=730, rate=0.25, profile="ramadan_sweets", pack=12, tier=3, margin="sweet"),
    "glucose": dict(sub="Miel & Sirop de Glucose", sup="SUP-CEVI", net=(500, "g"), price=260,
                    shelf=540, rate=0.3, profile="ramadan_baking", pack=12, tier=2, margin="dry"),
    "cereales": dict(sub="Céréales & Poudres Chocolatées", sup="SUP-EPIC", net=(375, "g"),
                     price=460, shelf=270, rate=0.5, profile="breakfast", pack=12, tier=2,
                     margin="sweet"),
    "poudre_choco": dict(sub="Céréales & Poudres Chocolatées", sup="SUP-EPIC", net=(400, "g"),
                         price=620, shelf=365, rate=0.5, profile="breakfast", pack=12, tier=2,
                         margin="sweet", overrides=[(r"Bimo", dict(net=(250, "g"), price=250))]),
    "lait_bebe": dict(sub="Alimentation Bébé", sup="SUP-EPIC", net=(400, "g"), price=1150,
                      shelf=540, rate=0.35, profile="baby", pack=12, tier=3, margin="baby",
                      overrides=[(r"Guigoz", dict(price=1320))]),
    "bebe_autres": dict(sub="Alimentation Bébé", sup="SUP-EPIC", net=(250, "g"), price=450,
                        shelf=365, rate=0.35, profile="baby", pack=12, tier=3, margin="baby",
                        overrides=[(r"Compote", dict(net=(130, "g"), price=120, rate_mult=1.5))]),
    "shampooing": dict(sub="Soins Cheveux", sup="SUP-DROG", net=(400, "ml"), price=330, shelf=None,
                       rate=0.6, profile="hygiene", pack=12, tier=2, margin="hygiene",
                       overrides=[(r"Gel Douche", dict(net=(500, "ml"), price=300)),
                                  (r"Masque", dict(price=420, rate_mult=0.5))]),
    "savon": dict(sub="Savons & Gels Douche", sup="SUP-DROG", net=(100, "g"), price=65,
                  shelf=None, rate=1.2, profile="hygiene", pack=48, tier=1, margin="hygiene",
                  overrides=[(r"Palmolive", dict(price=110))]),
    "dentifrice": dict(sub="Hygiène Bucco-Dentaire", sup="SUP-DROG", net=(75, "ml"), price=250,
                       shelf=None, rate=0.6, profile="hygiene", pack=12, tier=2, margin="hygiene",
                       overrides=[(r"Herbal", dict(net=(100, "ml"), price=150))]),
    "brosse": dict(sub="Hygiène Bucco-Dentaire", sup="SUP-DROG", net=(1, "pcs"), price=140,
                   shelf=None, rate=0.3, profile="hygiene", pack=12, tier=2, margin="hygiene"),
    "deodorant": dict(sub="Déodorants", sup="SUP-DROG", net=(50, "ml"), price=280, shelf=None,
                      rate=0.4, profile="hygiene_summer", pack=12, tier=2, margin="hygiene",
                      overrides=[(r"Spray", dict(net=(150, "ml"), price=460))]),
    "couches": dict(sub="Hygiène Bébé", sup="SUP-DROG", net=(40, "pcs"), price=1250, shelf=None,
                    rate=0.45, profile="baby", pack=6, tier=3, margin="baby",
                    overrides=[(r"Molfix", dict(price=1500))]),
    "lingettes": dict(sub="Hygiène Bébé", sup="SUP-DROG", net=(72, "pcs"), price=250, shelf=None,
                      rate=0.5, profile="baby", pack=24, tier=2, margin="hygiene"),
    "protections": dict(sub="Protections Féminines", sup="SUP-DROG", net=(10, "pcs"), price=190,
                        shelf=None, rate=0.7, profile="flat", pack=24, tier=3, margin="hygiene",
                        overrides=[(r"Always", dict(price=280))]),
    "papier": dict(sub="Papier & Emballage", sup="SUP-DROG", net=(4, "pcs"), price=190,
                   shelf=None, rate=0.8, profile="hygiene", pack=12, tier=1, margin="hygiene",
                   overrides=[(r"Mouchoirs Boîte", dict(net=(100, "pcs"), price=130)),
                              (r"Pochette", dict(net=(100, "pcs"), price=120)),
                              (r"Essuie-tout", dict(net=(2, "pcs"), price=180)),
                              (r"Aluminium", dict(net=(10, "m"), price=250, rate_mult=0.4)),
                              (r"Film", dict(net=(30, "m"), price=210, rate_mult=0.3))]),
    "lessive_poudre": dict(sub="Lessives", sup="SUP-DROG", net=(1000, "g"), price=290, shelf=None,
                           rate=0.8, profile="cleaning", pack=12, tier=2, margin="cleaning",
                           overrides=[(r"Le Chat", dict(price=340))]),
    "lessive_liquide": dict(sub="Lessives", sup="SUP-DROG", net=(2500, "ml"), price=1000,
                            shelf=None, rate=0.3, profile="cleaning", pack=4, tier=3,
                            margin="cleaning", overrides=[(r"Nadhif", dict(net=(3000, "ml"), price=820))]),
    "vaisselle": dict(sub="Vaisselle", sup="SUP-DROG", net=(750, "ml"), price=190, shelf=None,
                      rate=1.1, profile="cleaning", pack=12, tier=2, margin="cleaning"),
    "javel": dict(sub="Javel & Nettoyants", sup="SUP-DROG", net=(1000, "ml"), price=85, shelf=None,
                  rate=1.5, profile="cleaning", pack=12, tier=1, margin="cleaning"),
    "nettoyant_sol": dict(sub="Javel & Nettoyants", sup="SUP-DROG", net=(1000, "ml"), price=185,
                          shelf=None, rate=0.7, profile="cleaning", pack=12, tier=2,
                          margin="cleaning"),
    "nettoyants_specifiques": dict(sub="Javel & Nettoyants", sup="SUP-DROG", net=(500, "ml"),
                                   price=260, shelf=None, rate=0.25, profile="cleaning", pack=12,
                                   tier=2, margin="cleaning",
                                   overrides=[(r"Déboucheur", dict(net=(1000, "ml"), price=320))]),
    "accessoires": dict(sub="Accessoires Ménagers", sup="SUP-DROG", net=(1, "pcs"), price=200,
                        shelf=None, rate=0.5, profile="cleaning", pack=24, tier=1,
                        margin="cleaning", prefer="count",
                        overrides=[(r"Éponges", dict(net=(3, "pcs"), price=100)),
                                   (r"Spirale", dict(net=(2, "pcs"), price=80)),
                                   (r"Serpillère", dict(price=250, rate_mult=0.5)),
                                   (r"Poubelle 30L", dict(net=(10, "pcs"), price=120, rate_mult=2)),
                                   (r"Poubelle 50L", dict(net=(10, "pcs"), price=150))]),
    "desodorisant": dict(sub="Désodorisants", sup="SUP-DROG", net=(300, "ml"), price=310,
                         shelf=None, rate=0.35, profile="cleaning", pack=12, tier=1,
                         margin="cleaning", overrides=[(r"Air Wick|Désodorisant 300ml$",
                                                        dict(price=560, rate_mult=0.4))]),
    "insecticide": dict(sub="Insecticides", sup="SUP-DROG", net=(300, "ml"), price=450,
                        shelf=None, rate=0.4, profile="insecticide_summer", pack=12, tier=2,
                        margin="cleaning",
                        overrides=[(r"Diffuseur", dict(net=(1, "pcs"), price=620)),
                                   (r"Recharge", dict(net=(1, "pcs"), price=420)),
                                   (r"Anti-Fourmis", dict(net=(100, "g"), price=160)),
                                   (r"Raticide", dict(net=(150, "g"), price=210)),
                                   (r"Tablettes", dict(net=(20, "pcs"), price=160))]),
}

# Product name -> family, first match wins. Order matters where names share a prefix.
PRODUCT_RULES = [
    (r"^Lait UHT", "laits_uht"), (r"^Yaourt", "yaourt"), (r"^Fromage", "fromage"),
    (r"^Margarine", "margarine"), (r"^Smen", "smen"), (r"^Lben", "lben"), (r"^Crème", "creme"),
    (r"^Pâtes Alimentaires", "pates"), (r"^Couscous", "couscous"), (r"^Semoule", "semoule"),
    (r"^Farine", "farine"), (r"^Huile de Table", "huile_table"),
    (r"^Huile d'Olive", "huile_olive"), (r"^Légumes Secs", "legumes_secs"),
    (r"^Biscuits", "biscuits"), (r"^Gaufrettes", "gaufrettes"),
    (r"^Pâte à Tartiner", "pate_tartiner"), (r"^Chocolat", "chocolat"), (r"^Halwa", "halwa"),
    (r"^Confiture", "confiture"), (r"^Snacks", "chips"),
    (r"^Sodas|^Boissons Gazeuses", "soda"), (r"^Eau Minérale", "eau"), (r"^Jus", "jus"),
    (r"^Boissons Énergisantes", "energy"), (r"^Sirops", "sirops"),
    (r"^Double Concentré", "tomate"), (r"^Harissa", "harissa"), (r"^Sauces", "sauces"),
    (r"^Thon", "thon"), (r"^Olives", "olives"), (r"^Vinaigre", "vinaigre"),
    (r"^Épices", "epices"), (r"^Café Moulu", "cafe_moulu"), (r"^Café Soluble", "cafe_soluble"),
    (r"^Thé Vert", "the_vert"), (r"^Thé Noir", "the_sachets"), (r"^Sucre Blanc", "sucre"),
    (r"^Sucre|^Édulcorant", "sucre_autres"), (r"^Miel", "miel"),
    (r"^Sirop de Glucose", "glucose"), (r"^Céréales Bébé", "bebe_autres"),
    (r"^Céréales", "cereales"), (r"^Poudre Chocolatée", "poudre_choco"),
    (r"^Alimentation Bébé", "lait_bebe"), (r"^Compotes Bébé", "bebe_autres"),
    (r"^Shampooing|^Après-Shampooing", "shampooing"), (r"^Savon", "savon"),
    (r"^Dentifrice", "dentifrice"), (r"^Brosses", "brosse"), (r"^Déodorants", "deodorant"),
    (r"^Couches", "couches"), (r"^Lingettes", "lingettes"), (r"^Protections", "protections"),
    (r"^Papier Toilette|^Essuie-tout", "papier"), (r"^Lessive en Poudre", "lessive_poudre"),
    (r"^Lessive Liquide", "lessive_liquide"), (r"^Liquide Vaisselle", "vaisselle"),
    (r"^Eau de Javel", "javel"), (r"^Nettoyant Sol", "nettoyant_sol"),
    (r"^Nettoyants Spécifiques", "nettoyants_specifiques"), (r"^Accessoires", "accessoires"),
    (r"^Désodorisant", "desodorisant"), (r"^Insecticides", "insecticide"),
]

# Product-level supplier overrides: the brand decides who delivers it.
SUPPLIER_RULES = [
    (r"Cevital|Fleurial", "SUP-CEVI"),
    (r"Tchin Candia", "SUP-LAIT"),
]

# ---------------------------------------------------------------------------------------

MASS = re.compile(r"(?:(\d+)\s*x\s*)?(\d+(?:[.,]\d+)?)\s*(kg|g|L|l|ml|cl)(?![A-Za-zÀ-ÿ])")
COUNT = re.compile(r"(?:(\d+)\s*x\s*)?(\d+)\s*(p|pièces?|Sachets|Rouleaux|mètres)(?![A-Za-zÀ-ÿ])")
TO_BASE = {"kg": (1000, "g"), "g": (1, "g"), "L": (1000, "ml"), "l": (1000, "ml"),
           "ml": (1, "ml"), "cl": (10, "ml"), "p": (1, "pcs"), "pièce": (1, "pcs"),
           "pièces": (1, "pcs"), "Sachets": (1, "pcs"), "Rouleaux": (1, "pcs"),
           "mètres": (1, "m")}
NICE_PACKS = [1, 2, 4, 6, 10, 12, 20, 24, 30, 48]


def parse_net(variant, prefer):
    """The net content in a variant name: (amount, unit) with unit g, ml, pcs or m."""
    patterns = [COUNT, MASS] if prefer == "count" else [MASS, COUNT]
    for pattern in patterns:
        matches = list(pattern.finditer(variant))
        if matches:
            multiplier, amount, unit = matches[-1].groups()
            factor, base = TO_BASE[unit]
            value = float(amount.replace(",", ".")) * factor * int(multiplier or 1)
            return round(value), base
    return 1, "pcs"


def family_for(product):
    for pattern, key in PRODUCT_RULES:
        if re.search(pattern, product):
            return key
    raise SystemExit(f"No family rule matches product '{product}'.")


def resolved(family, product, variant):
    """The family's attributes, then the brand's supplier, then variant overrides."""
    attrs = {k: v for k, v in family.items() if k != "overrides"}
    attrs["rate_mult"] = 1.0
    for pattern, supplier in SUPPLIER_RULES:
        if re.search(pattern, product):
            attrs["sup"] = supplier
            break
    for pattern, override in family.get("overrides", []):
        if re.search(pattern, variant):
            attrs.update(override)
    return attrs


def round_price(value):
    """Shelf prices end in 5 below 100 DZD and in 0 from 100 up."""
    step = 5 if value < 100 else 10
    return max(step, int(round(value / step)) * step)


def nice_pack(value):
    return min(NICE_PACKS, key=lambda n: abs(math.log(n) - math.log(max(value, 1))))


def normal(rng):
    u1, u2 = rng.random() or 1e-12, rng.random()
    return math.sqrt(-2.0 * math.log(u1)) * math.cos(2.0 * math.pi * u2)


def ean13(twelve):
    total = sum(int(d) * (1 if i % 2 == 0 else 3) for i, d in enumerate(twelve))
    return twelve + str((10 - total % 10) % 10)


def fmt_net(amount, unit):
    return f"{amount} {unit}"


def dzd(value):
    return f"{value:.2f}"


def main():
    with open(SOURCE, encoding="utf-8-sig", newline="") as f:
        rows = list(csv.reader(f))
    header, rows = rows[0], rows[1:]
    assert header == ["Category", "Product_Name", "Variant"], header

    variants = []
    first_variant_of = {}
    for line, row in enumerate(rows, start=2):
        if len(row) == 4:  # "Café, Thé & Petit Déjeuner" was not quoted
            row = [row[0] + "," + row[1], row[2], row[3]]
        category, product, variant = (cell.strip() for cell in row)
        category = CATEGORY_FIXES.get(category, category)
        key = family_for(product)
        attrs = resolved(F[key], product, variant)
        parent, vat = SUBCATEGORIES[attrs["sub"]]
        if parent != category:
            raise SystemExit(f"line {line}: '{product}' maps to {attrs['sub']} under {parent}, "
                             f"but the source says {category}.")
        first_variant_of.setdefault(product, len(variants))
        variants.append(dict(line=line, category=category, product=product, variant=variant,
                             family=key, attrs=attrs, vat=vat))

    rng_rates = []
    for index, v in enumerate(variants):
        rng = random.Random(f"{SEED}:{index}")
        a = v["attrs"]
        net = parse_net(v["variant"], a.get("prefer"))
        ref_amount, ref_unit = a["net"]
        size_ratio = net[0] / ref_amount if net[1] == ref_unit and ref_amount > 0 else 1.0

        if "regulated" in a and net[1] == ref_unit and net[0] in a["regulated"]:
            retail = a["regulated"][net[0]]
        else:
            exponent = 0.88 if size_ratio >= 1 else 0.6
            raw = a["price"] * size_ratio ** exponent * (1 + rng.uniform(-0.06, 0.06))
            retail = round_price(raw)

        low, high = MARGINS[a["margin"]]
        margin = rng.uniform(low, high)
        net_retail = retail / (1 + v["vat"] / 10000)
        cost = round(net_retail * (1 - margin), 2)

        flagship = 1.3 if first_variant_of[v["product"]] == index else 1.0
        size_factor = min(1.8, max(0.2, size_ratio ** (-0.6 if size_ratio >= 1 else -0.35)))
        jitter = min(2.0, max(0.5, math.exp(0.35 * normal(rng))))
        rate = a["rate"] * a["rate_mult"] * size_factor * flagship * jitter
        rng_rates.append(rate)

        pack = nice_pack(a["pack"] / size_ratio if size_ratio > 0 else a["pack"])
        v.update(net=net, retail=retail, cost=cost, pack=pack)

    scale = TARGET_UNITS_PER_DAY / sum(rng_rates)
    for v, rate in zip(variants, rng_rates):
        v["rate"] = round(rate * scale, 3)

    for index, v in enumerate(variants, start=1):
        rng = random.Random(f"{SEED}:off-step:{index}")
        off_step = "regulated" not in v["attrs"] and rng.random() < OFF_STEP_SHARE
        v["off_step"] = rng.randint(1, 4) if off_step else 0

    os.makedirs(OUT, exist_ok=True)
    products_seen = {}
    with open(os.path.join(OUT, "catalogue.csv"), "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(["sku", "barcode", "category", "subcategory", "product_name", "variant_name",
                    "net_content", "selling_unit", "retail_price_dzd", "purchase_price_dzd",
                    "supplier_code", "purchase_unit", "units_per_purchase_unit",
                    "shelf_life_days", "base_daily_rate", "seasonality_profile",
                    "substitutability_tier"])
        for index, v in enumerate(variants, start=1):
            a = v["attrs"]
            supplier = products_seen.setdefault(v["product"], a["sup"])
            if supplier != a["sup"]:
                raise SystemExit(f"'{v['product']}' has variants from two suppliers.")
            w.writerow([f"GDZ-{index:04d}", ean13(f"{BARCODE_PREFIX}{index:09d}"), v["category"],
                        a["sub"], v["product"], v["variant"], fmt_net(*v["net"]), "piece",
                        dzd(v["retail"] + v["off_step"]), dzd(v["cost"]), supplier, "carton", v["pack"],
                        "" if a["shelf"] is None else a["shelf"], f"{v['rate']:.3f}",
                        a["profile"], a["tier"]])

    with open(os.path.join(OUT, "categories.csv"), "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(["category", "subcategory", "vat_bp", "sensitive", "sensitive_reason"])
        for sub, (parent, vat) in SUBCATEGORIES.items():
            w.writerow([parent, sub, vat, 0, ""])

    with open(os.path.join(OUT, "suppliers.csv"), "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(["supplier_code", "company_name", "delivery_days", "stated_lead_time_days",
                    "net_days"])
        for supplier in SUPPLIERS:
            w.writerow(supplier)

    changes = []
    span = (PRICE_CHANGE_WINDOW[1] - PRICE_CHANGE_WINDOW[0]).days
    for index, v in enumerate(variants, start=1):
        if "regulated" in v["attrs"]:
            continue
        rng = random.Random(f"{SEED}:changes:{index}")
        draw = rng.random()
        count = 2 if draw < 0.15 else 1 if draw < 0.65 else 0
        # Buckets 60 days wide, one change per bucket at most: two increases on one product
        # are never closer than a month and never outside the window.
        days = sorted(rng.sample(range(0, span - 29, 60), count)) if count else []
        retail, cost = v["retail"], v["cost"]
        for offset in days:
            ratio = 1 + rng.uniform(0.04, 0.10)
            new_retail = round_price(retail * ratio)
            if new_retail <= retail:
                new_retail = retail + (5 if retail < 100 else 10)
            cost = round(cost * new_retail / retail, 2)
            retail = new_retail
            when = PRICE_CHANGE_WINDOW[0] + timedelta(days=offset + rng.randrange(0, 30))
            changes.append((f"GDZ-{index:04d}", when.isoformat(), dzd(retail + v["off_step"]), dzd(cost)))

    with open(os.path.join(OUT, "price_changes.csv"), "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(["sku", "effective_date", "retail_price_dzd", "purchase_price_dzd"])
        w.writerows(changes)

    print(f"{len(variants)} variants, {len(products_seen)} products, "
          f"{len(SUBCATEGORIES)} subcategories, {len(changes)} price changes, "
          f"scale {scale:.3f}")


if __name__ == "__main__":
    main()
