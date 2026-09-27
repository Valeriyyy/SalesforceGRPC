import { mount, type Component } from "svelte";
import "../assets/css/app.css";

import PageHost from "../lib/shell/PageHost.svelte";
import type { PageView } from "../lib/views";
import OrgConnection from "./org-connection/OrgConnection.svelte";
import Overview from "./overview/Overview.svelte";
import Placeholder from "./Placeholder.svelte";

// Every page is wrapped in the shared Shell, and receives the page half of the PageView its view controller
// built (ADR 0006). The key is the component name the controller passes to SveltePage.For.
const componentRegistry: Record<string, Component<{ page: any }>> = {
    overview: Overview,
    placeholder: Placeholder,
    "org-connection": OrgConnection
};

function decodeProps(encodedProps: string): PageView<unknown> | null {
    try {
        // base64 of UTF-8 JSON: decode the bytes as UTF-8, not as Latin-1 as atob alone would.
        const bytes = Uint8Array.from(atob(encodedProps), c => c.charCodeAt(0));
        return JSON.parse(new TextDecoder().decode(bytes));
    } catch(error) {
        console.error("failed to decode props:", error);
        return null;
    }
}

const mountPoint = document.getElementById("app");

if(!mountPoint) {
    console.error("mount point #app not found");
} else {
    const componentName = mountPoint.dataset.component;
    const page = componentName ? componentRegistry[componentName] : undefined;
    const view = decodeProps(mountPoint.dataset.props ?? "");

    if(!componentName) {
        console.error("data-component attribute not found on mount point");
    } else if(!page) {
        console.error(`component "${componentName}" not found in registry`);
    } else if(!view) {
        console.error(`no page view for component "${componentName}"`);
    } else {
        mount(PageHost, {
            target: mountPoint,
            props: { page, view }
        });
    }
}
