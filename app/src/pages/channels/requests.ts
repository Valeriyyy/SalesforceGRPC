// The bodies the Channels pages send to api/PlatformEventManagement and api/Bindings, beyond the view models.

/** POST api/PlatformEventManagement/channels/data. */
export interface NewChannel {
    fullName: string;
    label: string;
    startingPoint: "Latest" | "Earliest";
    makePrimary: boolean;
}

/** Its answer. */
export interface NewChannelResult {
    channelId: number;
    adopted: boolean;
    memberCount: number;
    fullName: string;
}

/** POST api/PlatformEventManagement/channels/{id}/members/batch. */
export interface AddChannelMembers {
    members: { selectedEntity: string }[];
}

/** Its answer: all added, or none, with each Entity's outcome. */
export interface AddChannelMembersResult {
    added: boolean;
    outcomes: { selectedEntity: string; status: "Added" | "Failed" | "NotAdded"; message: string | null; memberId: number | null }[];
    leftInSalesforce: string[];
}

/** PATCH api/PlatformEventManagement/members/{id}. Both replace the member's current values. */
export interface UpdateChannelMember {
    filterExpression: string | null;
    enrichedFields: string[];
}

/** GET api/PlatformEventManagement/selectable-entities?channelType=data. */
export interface SelectableEntity {
    value: string;
    label: string;
}

/** PUT api/Bindings/primary-channel. Start only matters when the Channel has a Checkpoint. */
export interface SetPrimaryChannel {
    channelId: number;
    start: "Resume" | "Earliest" | "Latest";
}

export const ChannelSuffix = "__chn";

/**
 * The same rule the server applies to a Channel's name before the suffix: starts with a letter, then letters,
 * numbers and single underscores, and does not end with one.
 */
const developerNamePattern = /^[A-Za-z][A-Za-z0-9]*(_[A-Za-z0-9]+)*$/;

export function developerNameProblem(name: string): string | null {
    if (name.length === 0) {
        return "Enter a name.";
    }
    if (name.length > 40) {
        return "Keep it to 40 characters or fewer.";
    }
    return developerNamePattern.test(name)
        ? null
        : "Start with a letter, use only letters, numbers and single underscores, and don't end with an underscore.";
}

/** The name Setup would suggest for a Label: spaces and punctuation become single underscores. */
export function developerNameFor(label: string): string {
    const name = label
        .normalize("NFKD")
        .replace(/[^A-Za-z0-9]+/g, "_")
        .replace(/^_+|_+$/g, "")
        .slice(0, 40)
        .replace(/_+$/g, "");
    return name.length > 0 && /^[0-9]/.test(name) ? `X${name}`.slice(0, 40) : name;
}

export const plural = (n: number, noun: string) => `${n} ${noun}${n === 1 ? "" : "s"}`;
