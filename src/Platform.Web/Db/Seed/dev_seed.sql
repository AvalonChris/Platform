-- Placeholder catalog for local development. Names, specs and prices are not real.

insert into products (slug, sku, name, kind, short_spec, description, price, stock_quantity, is_featured, is_new, sort_order)
values
    ('nmn', 'NMN-500-60', 'NMN', 'single', '500 mg · 60 capsules', '[Product description]', 48.00, 100, true, false, 10),
    ('creatine-monohydrate', 'CRE-5G-60', 'Creatine Monohydrate', 'single', '5 g · 60 servings', '[Product description]', 32.00, 100, false, false, 20),
    ('omega-3', 'OM3-1000-60', 'Omega-3 EPA/DHA', 'single', '1,000 mg · 60 softgels', '[Product description]', 36.00, 100, true, false, 30),
    ('magnesium-glycinate', 'MAG-200-90', 'Magnesium Glycinate', 'single', '200 mg · 90 capsules', '[Product description]', 28.00, 100, true, false, 40),
    ('vitamin-d3-k2', 'D3K2-60', 'Vitamin D3 + K2', 'single', '2,000 IU / 100 mcg · 60 capsules', '[Product description]', 24.00, 100, false, true, 50),
    ('coq10-ubiquinol', 'Q10-100-60', 'CoQ10 Ubiquinol', 'single', '100 mg · 60 softgels', '[Product description]', 42.00, 100, false, true, 60),
    ('foundation-stack', 'STK-FOUNDATION', 'Foundation Stack', 'stack', '3 products · 30-day supply', '[Stack description]', 76.00, 0, true, false, 70),
    ('full-daily-stack', 'STK-DAILY', 'Full Daily Stack', 'stack', '4 products · 30-day supply', '[Stack description]', 124.00, 0, false, true, 80)
on conflict (slug) do nothing;

insert into stack_items (stack_product_id, component_product_id)
select s.id, c.id
from (values
    ('foundation-stack', 'omega-3'),
    ('foundation-stack', 'magnesium-glycinate'),
    ('foundation-stack', 'vitamin-d3-k2'),
    ('full-daily-stack', 'nmn'),
    ('full-daily-stack', 'creatine-monohydrate'),
    ('full-daily-stack', 'omega-3'),
    ('full-daily-stack', 'magnesium-glycinate')
) as pairs (stack_slug, component_slug)
join products s on s.slug = pairs.stack_slug
join products c on c.slug = pairs.component_slug
on conflict do nothing;
