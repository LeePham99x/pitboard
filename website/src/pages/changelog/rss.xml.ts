import rss from "@astrojs/rss";
import type { APIContext } from "astro";
import { siteData } from "../../lib/data.ts";

export async function GET(context: APIContext) {
  const { versions } = await siteData();
  return rss({
    title: "Pitboard releases",
    description: "Each Pitboard release, with what it added, changed and fixed.",
    site: context.site!,
    trailingSlash: false,
    items: versions.map((v) => ({
      title: `Pitboard ${v.version}${v.prerelease ? " (pre-release)" : ""}`,
      link: `/changelog#v${v.version}`,
      // the maintainer's date, at midnight in UTC+7, where releases are made
      pubDate: new Date(`${v.date}T00:00:00+07:00`),
      content: [v.intro ?? "", ...v.sections.map((s) => `<h3>${s.kind}</h3>${s.html}`)].join(""),
    })),
  });
}
