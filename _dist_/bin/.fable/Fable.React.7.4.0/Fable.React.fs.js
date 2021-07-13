import { Record } from "../fable-library.3.1.15/Types.js";
import { record_type, string_type } from "../fable-library.3.1.15/Reflection.js";

export class FragmentProps extends Record {
    constructor(key) {
        super();
        this.key = key;
    }
}

export function FragmentProps$reflection() {
    return record_type("Fable.React.FragmentProps", [], FragmentProps, () => [["key", string_type]]);
}

