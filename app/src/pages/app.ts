import { mount } from "svelte";
import "../assets/css/app.css";

// import Layout from "../layouts/Layout.svelte";

import App from "./App.svelte";

const componentRegistry: Record<string, any> = {
    app: App
};

function decodeProps(encodedProps: string): any {
    if(!encodedProps || encodedProps === "{}") {
        return {};
    }

    try {
        const decodedJson = atob(encodedProps);
        return JSON.parse(decodedJson);
    } catch(error) {
        console.error("failed to decode props:", error);
        return {};
    }
}

const mountPoint = document.getElementById("app");

if(!mountPoint) {
    console.error("mount point #app not found");
} else {
    const componentName = mountPoint.dataset.component;
    console.log(componentName);

    if(!componentName) {
        console.error("data-component attribute not found on mount point");
    } else if(!componentRegistry[componentName]) {
        console.error(`component "${componentName}" not found in registry`);
    } else {
        const encodedProps = mountPoint.dataset.props || "{}";
        const props = decodeProps(encodedProps);

        mount(componentRegistry[componentName], {
            target: mountPoint,
            props
        });
    }
}