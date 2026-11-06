import { Banner } from "@fleetops/ui";
import { en } from "../i18n/en";

export function ExpiredNotice() {
  return <Banner label={en.banner.error}>{en.auth.expired}</Banner>;
}
