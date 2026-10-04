-- Launch catalogue for local development, from the Supliful products in docs/supliful-catalog.md.
-- Prices are the launch prices agreed on 2026-10-03. Running the seed again resets these products to
-- these values, overwriting edits made in the admin.

insert into products (slug, sku, name, kind, short_spec, description, price, stock_quantity,
                      supplier_cost, shipping_weight_lb, costs_updated_at,
                      is_active, is_featured, is_new, sort_order, ingredients, suggested_use, warnings)
values
    ('nmn', 'NMN-500-30', 'NMN', 'single',
     '500 mg · 30 capsules · 30-day supply',
     'NMN (β-nicotinamide mononucleotide) is a precursor to NAD+, a coenzyme involved in cellular energy metabolism. NAD+ levels naturally decline with age. One capsule a day provides 500 mg of NMN, with no unnecessary additives.*',
     29.99, 0, 7.65, 0.1, '2026-10-03', true, true, false, 10,
     'β-Nicotinamide Mononucleotide (500 mg), HPMC (vegetable capsule), Microcrystalline Cellulose, Silicon Dioxide, Magnesium Stearate.',
     'As a dietary supplement, adults take one (1) capsule daily. For best results, take with 6-8 oz (177-237ml) of water or as directed by healthcare professional.',
     'Do not exceed recommended dose. Consult a physician if pregnant, nursing, under 18, or have a medical condition. Keep out of reach of children. Do not use if safety seal is damaged or missing. Store in a cool, dry place.'),

    ('resveratrol', 'RES-600-60', 'Resveratrol', 'single',
     '300 mg trans-resveratrol per capsule · 60 capsules · 30-day supply',
     'Resveratrol is an antioxidant from the root of the Japanese knotweed plant (Polygonum cuspidatum). Each capsule provides 600 mg of resveratrol complex standardized to 50% trans-resveratrol. Two capsules a day help protect cells from oxidative stress and support healthy aging.*',
     19.99, 0, 8.65, 0.15, '2026-10-03', true, true, false, 30,
     'Resveratrol (Polygonum cuspidatum)(root) Complex Containing 50% Trans-Resveratrol (600mg), Hypromellose (vegetable capsule), Microcrystalline Cellulose.',
     'Take one (1) veggie capsule twice a day as a dietary supplement. For best results, take 20-30 min before a meal or as directed by your healthcare professional.',
     'Do not exceed recommended dose. Pregnant or nursing mothers, children under the age of 18, and individuals with a known medical condition should consult a physician before using this or any dietary supplement. Keep out of reach of children. Do not use if the safety seal is damaged or missing. Store in a cool, dry place.'),

    ('coq10', 'Q10-200-30', 'CoQ10', 'single',
     '200 mg ubiquinone · 30 capsules · 30-day supply',
     'Coenzyme Q10 is found in every cell of the body and plays a key role in converting food into energy. Natural levels may decline with age. One capsule a day provides 200 mg of CoQ10 as ubiquinone to support energy production, antioxidant defenses and heart health. Suitable for vegetarians.*',
     19.99, 0, 8.79, 0.1, '2026-10-03', true, false, false, 40,
     'Coenzyme Q-10 (Ubiquinone) 200mg, Hypromellose (vegetable capsule), Rice Flour.',
     'Take one (1) capsule once a day as a dietary supplement. For best results, take 20-30 min before a meal or as directed by your healthcare professional.',
     'Do not exceed recommended dose. Pregnant or nursing mothers, children under the age of 18, and individuals with a known medical condition should consult a physician before using this or any dietary supplement. Keep out of reach of children. Do not use if the safety seal is damaged or missing. Store in a cool, dry place.'),

    ('magnesium-glycinate', 'MAG-275-90', 'Magnesium Glycinate', 'single',
     '275 mg magnesium per serving · 90 capsules · 30-day supply',
     'Magnesium takes part in hundreds of processes in the body, including nerve and muscle function and energy production. Three capsules a day provide 275 mg of magnesium from magnesium glycinate, a gentle, well-tolerated form, to support relaxation and healthy sleep.*',
     19.99, 0, 8.95, 0.25, '2026-10-03', true, false, false, 50,
     'Magnesium (from 2,500mg Magnesium Glycinate) 275mg, Hypromellose (capsule), Magnesium Stearate, Silicon Dioxide, Rice Flour.',
     'As a dietary supplement, take three (3) capsules once daily or as directed by your healthcare professional.',
     'Do not exceed recommended dose. Pregnant or nursing mothers, children under the age of 18, and individuals with a known medical condition should consult a physician before using this or any dietary supplement. Keep out of reach of children. Do not use if the safety seal is damaged or missing. Store in a cool, dry place.'),

    -- Second wave: hidden at launch.
    ('creatine-monohydrate', 'CRE-5G-50', 'Creatine Monohydrate', 'single',
     '5 g per serving · 281 g · 50 servings',
     'Creatine monohydrate, unflavored. One scoop provides 5,000 mg of creatine monohydrate.*',
     34.99, 0, 12.35, 0.625, '2026-10-03', false, false, false, 90,
     'Creatine Monohydrate.',
     'As a dietary supplement, adults take one (1) scoop in eight (8) oz. of water or juice four (4) times daily during the first five (5) days (loading phase). After the loading phase, take one (1) or two (2) times daily or as directed by a health care professional.',
     'Keep out of reach of children. Do not use if the safety seal is damaged or missing. Store in a cool, dry place and away from direct light.'),

    ('nmn-3-month-supply', 'STK-NMN-3MO', 'NMN 3-Month Supply', 'stack',
     '3 bottles · 500 mg a day · 90-day supply',
     'Three bottles of NMN in one shipment: a 90-day supply at 500 mg a day, for less than buying them one at a time.*',
     59.99, 0, null, null, null, true, true, false, 20,
     '', '', ''),

    ('longevity-stack', 'STK-LONGEVITY', 'Longevity Stack', 'stack',
     'NMN + resveratrol · 30-day supply',
     'NMN and resveratrol together: a 30-day supply of each, for less than buying them separately.*',
     44.99, 0, null, null, null, true, true, false, 25,
     '', '', '')
on conflict (slug) do update set
    sku = excluded.sku,
    name = excluded.name,
    kind = excluded.kind,
    short_spec = excluded.short_spec,
    description = excluded.description,
    price = excluded.price,
    stock_quantity = excluded.stock_quantity,
    supplier_cost = excluded.supplier_cost,
    shipping_weight_lb = excluded.shipping_weight_lb,
    costs_updated_at = excluded.costs_updated_at,
    is_active = excluded.is_active,
    is_featured = excluded.is_featured,
    is_new = excluded.is_new,
    sort_order = excluded.sort_order,
    ingredients = excluded.ingredients,
    suggested_use = excluded.suggested_use,
    warnings = excluded.warnings,
    updated_at = now();

delete from stack_items
where stack_product_id in (select id from products where slug in ('nmn-3-month-supply', 'longevity-stack'));

insert into stack_items (stack_product_id, component_product_id, quantity)
select s.id, c.id, contents.quantity
from (values
    ('nmn-3-month-supply', 'nmn', 3),
    ('longevity-stack', 'nmn', 1),
    ('longevity-stack', 'resveratrol', 1)
) as contents (stack_slug, component_slug, quantity)
join products s on s.slug = contents.stack_slug
join products c on c.slug = contents.component_slug;

-- Placeholder products from the first prototype, kept only because past test orders refer to them.
update products
set is_active = false, is_featured = false, is_new = false, updated_at = now()
where slug in ('omega-3', 'vitamin-d3-k2', 'coq10-ubiquinol', 'foundation-stack', 'full-daily-stack');
